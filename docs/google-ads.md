# Google Ads integration boundary

`IGoogleAdsClient` exposes only snapshot retrieval and pause-to-state. `SimulatedGoogleAdsClient` is the sole provider registered by the demo host. `GoogleAdsSdkClient` compiles against official SDK 27.3.0 / API V25; it is an integration starting point with explicit operational constraints, not a validated live deployment.

## Implemented in the SDK adapter

- Numeric customer/campaign ID validation before constructing resource names or GAQL.
- UTC account timezone check before reading aligned one-hour performance.
- GAQL filters for enabled campaigns, report date, and report hour.
- Cost micros converted directly to decimal. Fractional conversions are preserved. Google reports conversion values as floating point; conversion to decimal at the boundary cannot improve the precision of the upstream value.
- Existing campaign status lookup before mutation. Already paused is success; removed or missing campaigns are permanent failures.
- Field mask limited to `status`; mutation sets `PAUSED` and never toggles/re-enables.
- Cancellation and per-RPC deadlines, with SDK retries disabled so the workflow owns retry policy.
- Conservative transport error mapping: unavailable/deadline exceeded are transient; other statuses surface as permanent and need operator assessment.

## Live activation checklist

1. Validate GAQL and status mutation against a Google Ads test account using the pinned SDK/API version. Keep the application disconnected from production accounts until contract tests pass.
2. Supply an explicitly configured `GoogleAdsClient` using the official library's OAuth/developer-token configuration. Read credentials from an approved secret store; do not hardcode them. Manager login/customer selection must be unambiguous.
3. Replace the host's simulator registration with `GoogleAdsSdkClient` and remove the deliberate mode guard. Add campaign catalog ingestion: the current `/campaigns` endpoint and policy catalog use the simulator's durable table and do not synchronize a live account.
4. Confirm that `metrics.conversions_value` represents the business revenue being used for ROAS. If it represents lead scores or mixed conversion values, provide a revenue source instead.
5. Define timezone handling for non-UTC accounts, attribution lag, minimum data age, currency precision, reporting adjustments, quotas, and a stop mechanism. The current adapter rejects non-UTC accounts rather than silently shifting windows.
6. Use human approval first, with named operator identity, expiry, and revalidation. Restrict mutation credentials and enforce an account allowlist.

Read failures currently fail the evaluation run and are retried by the next scheduled tick. Pause retries are bounded in the workflow. Error details persisted to audit are sanitized categories, not full provider payloads or credentials. Internal operation IDs correlate the audit but do not provide Google-side exactly-once behavior.

Official references: [client library repository](https://github.com/googleads/google-ads-dotnet), [Google Ads .NET overview](https://developers.google.com/google-ads/api/docs/client-libs/dotnet/overview), [GAQL overview](https://developers.google.com/google-ads/api/docs/query/overview).
