using PaidTraffic.Domain;

namespace PaidTraffic.Api.Persistence;

public sealed class Campaign
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public CampaignStatus Status { get; set; }
    // These fields represent the simulated provider's durable state, not cached analytics.
    public decimal Spend { get; set; }
    public decimal Conversions { get; set; }
    public decimal Revenue { get; set; }
    public int TransientFailuresRemaining { get; set; }
    public bool PermanentFailure { get; set; }
}

public sealed class Policy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CampaignId { get; set; } = "";
    public decimal MaxSpendWithoutConversion { get; set; }
    public decimal? MinimumRoas { get; set; }
    public decimal MinimumSpendForRoas { get; set; }
    public string Currency { get; set; } = "USD";
    public ActionMode Mode { get; set; }
    public Guardrail ToGuardrail() => new(MaxSpendWithoutConversion, MinimumRoas, MinimumSpendForRoas, Currency, Mode);
}

public sealed class Evaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CampaignId { get; set; } = "";
    public Guid PolicyId { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public decimal Spend { get; set; }
    public decimal Conversions { get; set; }
    public decimal Revenue { get; set; }
    public string Currency { get; set; } = "USD";
    public bool Violated { get; set; }
    public string[] Reasons { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EvaluationId { get; set; }
    public string CampaignId { get; set; } = "";
    public IncidentStatus Status { get; set; }
    public ActionMode Mode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OperationalAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid? EvaluationId { get; set; }
    public Guid? ActionId { get; set; }
    public string Event { get; set; } = "";
    public string Actor { get; set; } = "system";
    public string Detail { get; set; } = "";
    public string? TraceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
