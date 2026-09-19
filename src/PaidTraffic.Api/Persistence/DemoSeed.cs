using Microsoft.EntityFrameworkCore;
using PaidTraffic.Domain;

namespace PaidTraffic.Api.Persistence;

public static class DemoSeed
{
    public static async Task InitializeAsync(TrafficDbContext db, CancellationToken ct)
    {
        if (await db.Campaigns.AnyAsync(ct)) return;
        db.Campaigns.AddRange(
            new Campaign { Id = "1001", Name = "Spend without return", Spend = 75 },
            new Campaign { Id = "1002", Name = "Approval required", Spend = 75 },
            new Campaign { Id = "1003", Name = "Healthy campaign", Spend = 40, Conversions = 4, Revenue = 200 },
            new Campaign { Id = "1004", Name = "Low ROAS", Spend = 100, Conversions = 2, Revenue = 50 },
            new Campaign { Id = "1005", Name = "Transient provider failure", Spend = 75, TransientFailuresRemaining = 1 },
            new Campaign { Id = "1006", Name = "Already paused", Spend = 75, Status = CampaignStatus.Paused },
            new Campaign { Id = "1007", Name = "Unconfigured campaign", Spend = 10, Revenue = 30, Conversions = 1 });
        for (var id = 1001; id <= 1006; id++)
            db.Policies.Add(new Policy
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"),
                CampaignId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                MaxSpendWithoutConversion = 50,
                MinimumRoas = id == 1004 ? 1.5m : null,
                MinimumSpendForRoas = 20,
                Mode = id == 1002 ? ActionMode.RequireApproval : ActionMode.Automatic
            });
        await db.SaveChangesAsync(ct);
    }
}
