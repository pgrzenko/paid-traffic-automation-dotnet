namespace PaidTraffic.Domain;

public enum ActionMode { Automatic, RequireApproval }
public enum CampaignStatus { Enabled, Paused }
public enum IncidentStatus { PendingApproval, Executing, Executed, Rejected, ExecutionFailed }

public sealed record Performance(decimal Spend, decimal Conversions, decimal Revenue, string Currency)
{
    public decimal? Roas => Spend == 0 ? null : Revenue / Spend;
    public static decimal FromMicros(long micros) => micros / 1_000_000m;

    public void Validate()
    {
        if (Spend < 0 || Conversions < 0 || Revenue < 0 || Currency.Length != 3)
            throw new ArgumentException("Performance requires nonnegative values and a three-letter currency.");
    }
}

public sealed record Guardrail(decimal MaxSpendWithoutConversion, decimal? MinimumRoas,
    decimal MinimumSpendForRoas, string Currency, ActionMode Mode)
{
    public void Validate()
    {
        if (MaxSpendWithoutConversion <= 0 || MinimumRoas is <= 0 || MinimumSpendForRoas < 0 ||
            Currency.Length != 3 || !Currency.All(char.IsAsciiLetterUpper) || !Enum.IsDefined(Mode))
            throw new ArgumentException("Invalid policy thresholds, currency, or action mode.");
    }
}

public sealed record RuleDecision(bool Violated, string[] Reasons);

public static class GuardrailEvaluator
{
    public static RuleDecision Evaluate(Performance performance, Guardrail policy)
    {
        performance.Validate();
        policy.Validate();
        if (performance.Currency != policy.Currency)
            throw new ArgumentException("Policy and performance currencies must match.");

        List<string> reasons = [];
        if (performance.Conversions == 0 && performance.Spend > policy.MaxSpendWithoutConversion)
            reasons.Add("MaxSpendWithoutConversion");
        if (policy.MinimumRoas is { } minimum && performance.Spend > 0 &&
            performance.Spend >= policy.MinimumSpendForRoas && performance.Roas < minimum)
            reasons.Add("MinimumRoas");
        return new(reasons.Count > 0, reasons.ToArray());
    }
}

public static class IncidentTransitions
{
    public static IncidentStatus Approve(IncidentStatus status) => status == IncidentStatus.PendingApproval
        ? IncidentStatus.Executing : throw new InvalidOperationException("Only pending incidents can be approved.");
    public static IncidentStatus Reject(IncidentStatus status) => status == IncidentStatus.PendingApproval
        ? IncidentStatus.Rejected : throw new InvalidOperationException("Only pending incidents can be rejected.");
    public static IncidentStatus Retry(IncidentStatus status) => status == IncidentStatus.ExecutionFailed
        ? IncidentStatus.Executing : throw new InvalidOperationException("Only failed incidents can be retried.");
}
