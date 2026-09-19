using PaidTraffic.Domain;
using Xunit;

namespace PaidTraffic.Domain.Tests;

public class GuardrailTests
{
    private static readonly Guardrail Policy = new(50m, 1.5m, 20m, "USD", ActionMode.Automatic);

    [Theory]
    [InlineData(50, 0, 100, false)]
    [InlineData(50.01, 0, 100, true)]
    [InlineData(75, 1, 150, false)]
    [InlineData(75, 0, 0, true)]
    [InlineData(20, 1, 30, false)]
    [InlineData(20, 1, 29.99, true)]
    [InlineData(19.99, 1, 0, false)]
    [InlineData(0, 0, 0, false)]
    [InlineData(75, 0.5, 150, false)]
    public void Evaluates_boundaries(decimal spend, decimal conversions, decimal revenue, bool violated) =>
        Assert.Equal(violated, GuardrailEvaluator.Evaluate(new(spend, conversions, revenue, "USD"), Policy).Violated);

    [Fact]
    public void Uses_decimal_micros_without_rounding() => Assert.Equal(75.000001m, Performance.FromMicros(75_000_001));

    [Fact]
    public void Zero_spend_has_no_roas() => Assert.Null(new Performance(0, 0, 5, "USD").Roas);

    [Fact]
    public void Rejects_currency_mismatch() => Assert.Throws<ArgumentException>(() =>
        GuardrailEvaluator.Evaluate(new(75, 0, 0, "EUR"), Policy));

    [Fact]
    public void Rejects_negative_spend() => Assert.Throws<ArgumentException>(() =>
        GuardrailEvaluator.Evaluate(new(-1, 0, 0, "USD"), Policy));

    [Theory]
    [InlineData(IncidentStatus.Executed)]
    [InlineData(IncidentStatus.Rejected)]
    [InlineData(IncidentStatus.Executing)]
    [InlineData(IncidentStatus.ExecutionFailed)]
    public void Approval_cannot_repeat(IncidentStatus status) =>
        Assert.Throws<InvalidTransitionException>(() => IncidentTransitions.Approve(status));

    [Fact]
    public void Valid_transitions_are_explicit()
    {
        Assert.Equal(IncidentStatus.Executing, IncidentTransitions.Approve(IncidentStatus.PendingApproval));
        Assert.Equal(IncidentStatus.Rejected, IncidentTransitions.Reject(IncidentStatus.PendingApproval));
        Assert.Equal(IncidentStatus.Executing, IncidentTransitions.Retry(IncidentStatus.ExecutionFailed));
        Assert.Throws<InvalidTransitionException>(() => IncidentTransitions.Retry(IncidentStatus.Executed));
    }
}
