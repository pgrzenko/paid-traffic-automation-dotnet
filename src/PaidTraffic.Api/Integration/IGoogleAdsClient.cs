using PaidTraffic.Domain;

namespace PaidTraffic.Api.Integration;

public sealed record CampaignSnapshot(string CampaignId, CampaignStatus Status, Performance Performance,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd);
public enum PauseResult { Paused, AlreadyPaused }

/// <summary>All timestamps are UTC. Pause is an idempotent desired-state operation, not a toggle.</summary>
public interface IGoogleAdsClient
{
    Task<IReadOnlyList<CampaignSnapshot>> GetActiveSnapshotsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<PauseResult> PauseCampaignAsync(string campaignId, Guid operationId, CancellationToken ct);
}

public sealed class TransientAdsException(string message) : Exception(message);
public sealed class PermanentAdsException(string message) : Exception(message);
