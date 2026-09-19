using System.Globalization;
using Google.Ads.GoogleAds.Lib;
using Google.Ads.GoogleAds.V25.Enums;
using Google.Ads.GoogleAds.V25.Resources;
using Google.Ads.GoogleAds.V25.Services;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using PaidTraffic.Domain;
using AdsServices = Google.Ads.GoogleAds.Services;
using DomainStatus = PaidTraffic.Domain.CampaignStatus;

namespace PaidTraffic.Api.Integration;

/// <summary>
/// Compiled official-SDK adapter for one explicitly configured, UTC Google Ads account.
/// Not registered by the demo host. Credentials belong in GoogleAdsClient configuration, never this class.
/// Conversion value is only revenue when the account's conversion tracking defines it that way.
/// </summary>
public sealed class GoogleAdsSdkClient : IGoogleAdsClient
{
    private readonly GoogleAdsServiceClient query;
    private readonly CampaignServiceClient campaigns;
    private readonly string customerId;

    public GoogleAdsSdkClient(GoogleAdsClient client, string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId) || !customerId.All(char.IsAsciiDigit))
            throw new ArgumentException("Customer ID must contain digits only.", nameof(customerId));
        this.customerId = customerId;
        query = client.GetService(AdsServices.V25.GoogleAdsService);
        campaigns = client.GetService(AdsServices.V25.CampaignService);
    }

    private static CallSettings Settings(CancellationToken ct) => CallSettings.FromCancellationToken(ct)
        .WithExpiration(Expiration.FromTimeout(TimeSpan.FromSeconds(4)))
        .WithRetry(RetrySettings.FromExponentialBackoff(1, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), 1, _ => false));

    public async Task<IReadOnlyList<CampaignSnapshot>> GetActiveSnapshotsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        if (start.Offset != TimeSpan.Zero || start.Minute != 0 || start.Second != 0 || start.Ticks % TimeSpan.TicksPerSecond != 0 || end != start.AddHours(1))
            throw new ArgumentException("This adapter supports aligned one-hour UTC windows.");
        try
        {
            var account = await query.SearchAsync(new SearchGoogleAdsRequest { CustomerId = customerId, Query = "SELECT customer.time_zone FROM customer" }, Settings(ct)).FirstAsync(ct);
            if (account.Customer.TimeZone is not ("UTC" or "Etc/UTC"))
                throw new PermanentAdsException("Account timezone must be UTC for this adapter's evaluation windows.");
            var gaql = FormattableString.Invariant($"""
                SELECT campaign.id, campaign.status, customer.currency_code,
                       metrics.cost_micros, metrics.conversions, metrics.conversions_value
                FROM campaign WHERE campaign.status = 'ENABLED'
                AND segments.date = '{start:yyyy-MM-dd}' AND segments.hour = {start.Hour}
                """);
            List<CampaignSnapshot> result = [];
            await foreach (var row in query.SearchAsync(new SearchGoogleAdsRequest { CustomerId = customerId, Query = gaql }, Settings(ct)).WithCancellation(ct))
            {
                result.Add(new(row.Campaign.Id.ToString(CultureInfo.InvariantCulture), DomainStatus.Enabled,
                    new(Performance.FromMicros(row.Metrics.CostMicros), checked((decimal)row.Metrics.Conversions),
                        checked((decimal)row.Metrics.ConversionsValue), row.Customer.CurrencyCode), start, end));
            }
            return result;
        }
        catch (RpcException ex) { throw Classify(ex, ct); }
    }

    public async Task<PauseResult> PauseCampaignAsync(string campaignId, Guid operationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(campaignId) || !campaignId.All(char.IsAsciiDigit))
            throw new PermanentAdsException("Campaign ID must contain digits only.");
        try
        {
            var gaql = $"SELECT campaign.status FROM campaign WHERE campaign.id = {campaignId}";
            var existing = await query.SearchAsync(new SearchGoogleAdsRequest { CustomerId = customerId, Query = gaql }, Settings(ct)).FirstOrDefaultAsync(ct);
            if (existing is null) throw new PermanentAdsException("Campaign does not exist.");
            if (existing.Campaign.Status == CampaignStatusEnum.Types.CampaignStatus.Paused) return PauseResult.AlreadyPaused;
            if (existing.Campaign.Status != CampaignStatusEnum.Types.CampaignStatus.Enabled)
                throw new PermanentAdsException("Campaign is not enabled or paused.");
            var operation = new CampaignOperation
            {
                Update = new Campaign
                {
                    ResourceName = $"customers/{customerId}/campaigns/{campaignId}",
                    Status = CampaignStatusEnum.Types.CampaignStatus.Paused
                },
                UpdateMask = new FieldMask { Paths = { "status" } }
            };
            // Google Ads offers no general idempotency key here. operationId is our audit correlation only.
            await campaigns.MutateCampaignsAsync(customerId, [operation], Settings(ct));
            return PauseResult.Paused;
        }
        catch (RpcException ex) { throw Classify(ex, ct); }
    }

    private static Exception Classify(RpcException error, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return new OperationCanceledException(ct);
        return error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
            ? new TransientAdsException($"Google Ads transport failure: {error.StatusCode}")
            : new PermanentAdsException($"Google Ads request failed: {error.StatusCode}");
    }
}
