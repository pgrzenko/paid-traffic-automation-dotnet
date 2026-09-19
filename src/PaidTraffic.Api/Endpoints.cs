using Microsoft.EntityFrameworkCore;
using PaidTraffic.Api.Operations;
using PaidTraffic.Api.Persistence;
using PaidTraffic.Domain;

namespace PaidTraffic.Api;

public sealed record CreatePolicy(string CampaignId, decimal MaxSpendWithoutConversion, decimal? MinimumRoas,
    decimal MinimumSpendForRoas, string Currency, ActionMode? Mode);

public sealed record IncidentDetails(Incident Incident, Evaluation Evaluation, OperationalAction Action, IReadOnlyList<AuditEntry> Audit);

public static class Endpoints
{
    public static void MapTrafficEndpoints(this WebApplication app, bool demo)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/campaigns", async (TrafficDbContext db, CancellationToken ct) =>
            await db.Campaigns.AsNoTracking().OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.Name, c.Status, c.Currency, c.Spend, c.Conversions, c.Revenue }).ToListAsync(ct));
        api.MapGet("/policies", async (TrafficDbContext db, CancellationToken ct) =>
            await db.Policies.AsNoTracking().OrderBy(p => p.CampaignId).ToListAsync(ct));
        api.MapPost("/policies", async (CreatePolicy request, TrafficDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.CampaignId) || string.IsNullOrWhiteSpace(request.Currency) || request.Mode is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["CampaignId, Currency, and an explicit Mode are required."] });
            var policy = new Policy
            {
                CampaignId = request.CampaignId,
                MaxSpendWithoutConversion = request.MaxSpendWithoutConversion,
                MinimumRoas = request.MinimumRoas,
                MinimumSpendForRoas = request.MinimumSpendForRoas,
                Currency = request.Currency,
                Mode = request.Mode.Value
            };
            policy.ToGuardrail().Validate();
            // Numeric inputs must fit the schema exactly; never silently round policy thresholds.
            var values = new[] { request.MaxSpendWithoutConversion, request.MinimumRoas ?? 0, request.MinimumSpendForRoas };
            if (values.Any(v => v >= 100_000_000_000_000m || decimal.Round(v, 6) != v))
                throw new ArgumentException("Thresholds support up to 14 integer and 6 fractional digits.");
            await using var gate = new WorkflowLock(db);
            await gate.AcquireAsync(ct);
            var campaign = await db.Campaigns.SingleOrDefaultAsync(c => c.Id == request.CampaignId, ct);
            if (campaign is null) return Results.NotFound();
            if (campaign.Currency != request.Currency) throw new ArgumentException("Policy currency must match campaign currency.");
            if (await db.Policies.AnyAsync(p => p.CampaignId == request.CampaignId, ct))
                return Results.Conflict(new { error = "A campaign has one immutable policy in this version." });
            db.Policies.Add(policy);
            db.Audit.Add(new AuditEntry { Event = "PolicyCreated", Actor = demo ? "demo-operator" : "api-key-operator", Detail = policy.Id.ToString(), CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/policies/{policy.Id}", policy);
        }).Produces<Policy>(201).ProducesValidationProblem().Produces(409);
        api.MapGet("/policies/{id:guid}", async (Guid id, TrafficDbContext db, CancellationToken ct) =>
            await db.Policies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) is { } policy ? Results.Ok(policy) : Results.NotFound());
        api.MapGet("/incidents", async (int? limit, TrafficDbContext db, CancellationToken ct) =>
            await db.Incidents.AsNoTracking().OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync(ct));
        api.MapGet("/incidents/{id:guid}", async (Guid id, TrafficDbContext db, CancellationToken ct) =>
        {
            var incident = await db.Incidents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
            if (incident is null) return Results.NotFound();
            return Results.Ok(new IncidentDetails(incident,
                await db.Evaluations.AsNoTracking().SingleAsync(e => e.Id == incident.EvaluationId, ct),
                await db.Actions.AsNoTracking().SingleAsync(a => a.IncidentId == id, ct),
                await db.Audit.AsNoTracking().Where(a => a.IncidentId == id || a.EvaluationId == incident.EvaluationId).OrderBy(a => a.Id).ToListAsync(ct)));
        }).Produces<IncidentDetails>().Produces(404);
        api.MapGet("/audit", async (long? after, TrafficDbContext db, CancellationToken ct) =>
            await db.Audit.AsNoTracking().Where(a => a.Id > (after ?? 0)).OrderBy(a => a.Id).Take(200).ToListAsync(ct));
        api.MapGet("/evaluations", async (TrafficDbContext db, CancellationToken ct) =>
            await db.Evaluations.AsNoTracking().OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Take(200).ToListAsync(ct));
        api.MapPost("/evaluations/run", async (TrafficWorkflow workflow, CancellationToken ct) => Results.Ok(await workflow.RunAsync(ct)))
            .Produces<EvaluationSummary>().ProducesProblem(409);
        foreach (var decision in new[] { "approve", "reject", "retry" })
        {
            api.MapPost($"/incidents/{{id:guid}}/{decision}", async (Guid id, TrafficWorkflow workflow, CancellationToken ct) =>
            {
                var incident = await workflow.DecideAsync(id, decision, demo ? "demo-operator" : "api-key-operator", ct);
                return incident is null ? Results.NotFound() : Results.Ok(incident);
            }).Produces<Incident>().Produces(404).ProducesProblem(409);
        }
    }
}
