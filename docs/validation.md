# Validation record

Validation was performed on 19 September 2026. The [CI workflow](../.github/workflows/ci.yml) is the executable verification contract; the [Actions history](https://github.com/pgrzenko/paid-traffic-automation-dotnet/actions/workflows/ci.yml) records results for each pushed revision.

## Local workstation

- Installed and used .NET SDK 10.0.401 after confirming that no SDK was initially present.
- Release solution build completed with zero warnings and zero errors.
- All 18 domain test cases passed.
- Formatting and migration/model drift checks passed.
- Both Compose configurations parsed successfully; registry lookup confirmed the collector image exists.
- NuGet vulnerability inspection, including transitive dependencies, reported no known vulnerable packages from the configured NuGet source.
- Git diff/whitespace checks and a source scan for common credential/private-key patterns were performed. Build output and test artifacts are ignored.

Docker Desktop on this Windows workstation failed to start because its inference manager could not access a stale runtime socket. Local Testcontainers cases failed during container fixture startup, before exercising application behavior. Host-level cleanup was not performed. This environment limitation must not be confused with a passing local Docker run.

## Linux validation

GitHub-hosted Linux runners provide the working container engine. CI restores the pinned dependency graph from a clean checkout, builds Release, checks formatting, runs real PostgreSQL Testcontainers integration tests, checks migrations, builds the Docker image, starts the full Compose demo, and executes the PowerShell assertions. The observability overlay also verifies receipt of a business counter and a pause span at an OTLP collector.

The final suite contains 18 domain cases and 14 PostgreSQL integration cases. Integration coverage includes:

- Automatic pause, healthy campaigns, low ROAS, duplicate runs, and source evidence.
- Human approval, rejection, and duplicate approval prevention.
- Transient retry, bounded exhaustion, permanent errors, and explicit operator retry.
- Injected database failure after provider success, followed by a new application host recovering the durable intent.
- Injected timeout after provider success, reconciled through `AlreadyPaused`.
- Database lock contention and uniqueness enforcement.
- A new hour while a pending incident exists.
- SQL-level append-only audit enforcement, including truncate.
- HTTP validation, required action authority, OpenAPI, health, and API-key protection.

## Not validated

- Live Google Ads authentication, GAQL execution, account configuration, or mutation. The official SDK adapter is compiled; no real advertising account was contacted.
- Sustained load, multi-region operation, database failover during an in-flight external call, and hostile privileged database administrators.
- Business suitability of one-hour windows, attribution delay, stale approvals, or conversion value as revenue for any real account.

These are explicit production integration and operating decisions, not capabilities inferred from the simulator.
