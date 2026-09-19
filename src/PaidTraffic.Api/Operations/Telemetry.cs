using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaidTraffic.Api.Operations;

public sealed class Telemetry : IDisposable
{
    public const string Name = "PaidTrafficAutomation";
    public ActivitySource Activities { get; } = new(Name);
    private readonly Meter meter = new(Name);
    public Counter<long> Evaluations { get; }
    public Counter<long> Violations { get; }
    public Counter<long> Actions { get; }
    public Counter<long> Failures { get; }
    public Histogram<double> Duration { get; }

    public Telemetry()
    {
        Evaluations = meter.CreateCounter<long>("campaign_evaluations_total", description: "New persisted snapshot/policy evaluations");
        Violations = meter.CreateCounter<long>("guardrail_violations_total", description: "New evaluations with at least one violated rule");
        Actions = meter.CreateCounter<long>("campaign_actions_total", description: "Persisted completed pause actions, including reconciliations");
        Failures = meter.CreateCounter<long>("campaign_action_failures_total", description: "Pause executions that exhausted retries or failed permanently");
        Duration = meter.CreateHistogram<double>("evaluation_duration", "s");
    }

    public void Dispose() { Activities.Dispose(); meter.Dispose(); }
}
