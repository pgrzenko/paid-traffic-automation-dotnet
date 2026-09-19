using Microsoft.EntityFrameworkCore;
using PaidTraffic.Api.Persistence;
using PaidTraffic.Domain;

namespace PaidTraffic.Api.Integration;

// A separate DbContext deliberately models an external side effect outside the workflow transaction.
public sealed class SimulatedGoogleAdsClient(IDbContextFactory<TrafficDbContext> factory) : IGoogleAdsClient
{
    public async Task<IReadOnlyList<CampaignSnapshot>> GetActiveSnapshotsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => c.Status == CampaignStatus.Enabled).OrderBy(c => c.Id).ToListAsync(ct);
        return campaigns.Select(c => new CampaignSnapshot(c.Id, c.Status,
            new(c.Spend, c.Conversions, c.Revenue, c.Currency), start, end)).ToArray();
    }

    public async Task<PauseResult> PauseCampaignAsync(string campaignId, Guid operationId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == campaignId, ct);
        if (campaign.Status == CampaignStatus.Paused) return PauseResult.AlreadyPaused;
        if (campaign.PermanentFailure) throw new PermanentAdsException("Provider denied campaign mutation.");
        if (campaign.TransientFailuresRemaining > 0)
        {
            campaign.TransientFailuresRemaining--;
            await db.SaveChangesAsync(ct);
            throw new TransientAdsException("Simulated temporary provider unavailability.");
        }
        campaign.Status = CampaignStatus.Paused;
        await db.SaveChangesAsync(ct);
        return PauseResult.Paused;
    }
}
