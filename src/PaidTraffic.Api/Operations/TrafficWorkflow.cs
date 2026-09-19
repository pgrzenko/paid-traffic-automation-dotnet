using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PaidTraffic.Api.Integration;
using PaidTraffic.Api.Persistence;
using PaidTraffic.Domain;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace PaidTraffic.Api.Operations;

public sealed record EvaluationSummary(int Evaluated, int CreatedIncidents, int CompletedActions);

public sealed class TrafficWorkflow(TrafficDbContext db, IGoogleAdsClient ads, Telemetry telemetry,
    TimeProvider clock, ILogger<TrafficWorkflow> logger)
{
    public async Task<EvaluationSummary> RunAsync(CancellationToken ct)
    {
        await using var gate = new WorkflowLock(db);
        await gate.AcquireAsync(ct);
        using var activity = telemetry.Activities.StartActivity("campaigns.evaluate");
        var started = Stopwatch.GetTimestamp();
        try
        {
            var completed = 0;
            // Durable intents left by a crash are reconciled before fetching new snapshots.
            foreach (var pending in await db.Incidents.Where(i => i.Status == IncidentStatus.Executing).OrderBy(i => i.CreatedAt).ToListAsync(ct))
                if (await ExecuteAsync(pending, ct)) completed++;

            var now = clock.GetUtcNow();
            var end = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
            var start = end.AddHours(-1);
            var snapshots = await ads.GetActiveSnapshotsAsync(start, end, ct);
            var policies = await db.Policies.AsNoTracking().ToDictionaryAsync(p => p.CampaignId, ct);
            var evaluated = 0;
            var created = 0;
            foreach (var snapshot in snapshots)
            {
                if (!policies.TryGetValue(snapshot.CampaignId, out var policy)) continue;
                if (await db.Evaluations.AnyAsync(e => e.CampaignId == snapshot.CampaignId && e.PolicyId == policy.Id && e.WindowStart == start && e.WindowEnd == end, ct)) continue;
                var decision = GuardrailEvaluator.Evaluate(snapshot.Performance, policy.ToGuardrail());
                var evaluation = new Evaluation
                {
                    CampaignId = snapshot.CampaignId,
                    PolicyId = policy.Id,
                    WindowStart = start,
                    WindowEnd = end,
                    Spend = snapshot.Performance.Spend,
                    Conversions = snapshot.Performance.Conversions,
                    Revenue = snapshot.Performance.Revenue,
                    Currency = snapshot.Performance.Currency,
                    Violated = decision.Violated,
                    Reasons = decision.Reasons,
                    CreatedAt = now
                };
                db.Evaluations.Add(evaluation);
                Audit("EvaluationRecorded", null, evaluation.Id, null, "system", string.Join(',', decision.Reasons));
                Incident? incident = null;
                if (decision.Violated && !await db.Incidents.AnyAsync(i => i.CampaignId == snapshot.CampaignId &&
                    (i.Status == IncidentStatus.PendingApproval || i.Status == IncidentStatus.Executing || i.Status == IncidentStatus.ExecutionFailed), ct))
                {
                    incident = new Incident
                    {
                        EvaluationId = evaluation.Id,
                        CampaignId = snapshot.CampaignId,
                        Mode = policy.Mode,
                        Status = policy.Mode == ActionMode.Automatic ? IncidentStatus.Executing : IncidentStatus.PendingApproval,
                        CreatedAt = now
                    };
                    db.Incidents.Add(incident);
                    db.Actions.Add(new OperationalAction { IncidentId = incident.Id });
                    Audit("IncidentCreated", incident.Id, evaluation.Id, null, "system", incident.Status.ToString());
                    created++;
                }
                await db.SaveChangesAsync(ct);
                telemetry.Evaluations.Add(1);
                if (decision.Violated) telemetry.Violations.Add(1);
                evaluated++;
                logger.LogInformation("Evaluated campaign {CampaignId}, evaluation {EvaluationId}, violated {Violated}", snapshot.CampaignId, evaluation.Id, decision.Violated);
                if (incident?.Status == IncidentStatus.Executing && await ExecuteAsync(incident, ct)) completed++;
            }
            return new(evaluated, created, completed);
        }
        finally { telemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds); }
    }

    public async Task<Incident?> DecideAsync(Guid id, string decision, string actor, CancellationToken ct)
    {
        await using var gate = new WorkflowLock(db);
        await gate.AcquireAsync(ct);
        var incident = await db.Incidents.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null) return null;
        incident.Status = decision switch
        {
            "approve" => IncidentTransitions.Approve(incident.Status),
            "reject" => IncidentTransitions.Reject(incident.Status),
            "retry" => IncidentTransitions.Retry(incident.Status),
            _ => throw new ArgumentException("Unknown decision.")
        };
        Audit("OperatorDecision", id, incident.EvaluationId, null, actor, decision);
        await db.SaveChangesAsync(ct);
        if (incident.Status == IncidentStatus.Executing) await ExecuteAsync(incident, ct);
        return incident;
    }

    private async Task<bool> ExecuteAsync(Incident incident, CancellationToken ct)
    {
        var action = await db.Actions.SingleAsync(a => a.IncidentId == incident.Id, ct);
        using var activity = telemetry.Activities.StartActivity("campaign.pause");
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["IncidentId"] = incident.Id, ["ActionId"] = action.Id, ["CampaignId"] = incident.CampaignId });
        var pipeline = new ResiliencePipelineBuilder<PauseResult>()
            .AddRetry(new RetryStrategyOptions<PauseResult>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                ShouldHandle = new PredicateBuilder<PauseResult>().Handle<TransientAdsException>().Handle<TimeoutRejectedException>()
            })
            .AddTimeout(TimeSpan.FromSeconds(5)).Build();

        PauseResult result;
        try
        {
            result = await pipeline.ExecuteAsync(async token =>
            {
                // Persist before EVERY external attempt, including retries. Failure here prevents dispatch.
                action.Attempts++;
                Audit("PauseAttempted", incident.Id, incident.EvaluationId, action.Id, "system", $"Attempt {action.Attempts}");
                await db.SaveChangesAsync(token);
                return await ads.PauseCampaignAsync(incident.CampaignId, action.Id, token);
            }, ct);
        }
        catch (Exception ex) when (ex is TransientAdsException or PermanentAdsException or TimeoutRejectedException)
        {
            incident.Status = IncidentStatus.ExecutionFailed;
            action.LastError = ex.GetType().Name;
            Audit("PauseFailed", incident.Id, incident.EvaluationId, action.Id, "system", action.LastError);
            await db.SaveChangesAsync(ct);
            telemetry.Failures.Add(1);
            logger.LogWarning("Campaign pause failed: {FailureType}", action.LastError);
            activity?.SetStatus(ActivityStatusCode.Error, action.LastError);
            return false;
        }
        // Persistence failures propagate, leaving durable Executing state for reconciliation.
        incident.Status = IncidentStatus.Executed;
        action.CompletedAt = clock.GetUtcNow();
        action.LastError = null;
        Audit("PauseCompleted", incident.Id, incident.EvaluationId, action.Id, "system", result.ToString());
        await db.SaveChangesAsync(ct);
        telemetry.Actions.Add(1, new KeyValuePair<string, object?>("result", result.ToString()));
        logger.LogInformation("Campaign pause completed with {Result}, total attempts {Attempts}", result, action.Attempts);
        return true;
    }

    private void Audit(string name, Guid? incident, Guid? evaluation, Guid? action, string actor, string detail) =>
        db.Audit.Add(new AuditEntry
        {
            IncidentId = incident,
            EvaluationId = evaluation,
            ActionId = action,
            Event = name,
            Actor = actor,
            Detail = detail,
            TraceId = Activity.Current?.TraceId.ToString(),
            CreatedAt = clock.GetUtcNow()
        });
}
