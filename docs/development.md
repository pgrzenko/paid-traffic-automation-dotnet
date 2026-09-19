# Development and configuration

## Run the host with the SDK

Install the SDK selected by `global.json` and start PostgreSQL:

```sh
docker compose up -d postgres
dotnet restore --locked-mode
dotnet tool restore
```

PowerShell:

```powershell
$env:ConnectionStrings__Traffic = 'Host=localhost;Database=traffic;Username=traffic;Password=local-demo-only'
$env:Demo__Enabled = 'true'
$env:Evaluation__Enabled = 'false'
dotnet run --project src/PaidTraffic.Api -- --migrate
dotnet run --project src/PaidTraffic.Api -- --urls http://localhost:8080
```

Bash:

```sh
export ConnectionStrings__Traffic='Host=localhost;Database=traffic;Username=traffic;Password=local-demo-only'
export Demo__Enabled=true
export Evaluation__Enabled=false
dotnet run --project src/PaidTraffic.Api -- --migrate
dotnet run --project src/PaidTraffic.Api -- --urls http://localhost:8080
```

Do not start the Compose API and the SDK host on the same port. The simulator database is seeded only by `--migrate` with explicit demo mode, and only if the campaign table is empty. Restarting preserves campaign state and history.

## Configuration

| Environment variable | Default | Purpose |
|---|---|---|
| `ConnectionStrings__Traffic` | Required | PostgreSQL connection string |
| `Demo__Enabled` | `false` | Explicit no-auth simulator demo mode and migration seed |
| `Security__ApiKey` | Required outside demo | Shared internal API key |
| `GoogleAds__Mode` | `Simulated` | Host rejects other values until live adapter is explicitly wired |
| `Evaluation__Enabled` | `true` | Periodic evaluation; Compose overrides to false |
| `Evaluation__IntervalSeconds` | `60` | Positive timer interval |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Unset | Enable OTLP trace/metric export |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | SDK default gRPC | Collector transport |
| `OTEL_METRIC_EXPORT_INTERVAL` | SDK default | Export cadence in milliseconds |

`EVALUATION_ENABLED` is a Compose interpolation variable, while `Evaluation__Enabled` is the host setting. The demo uses known disposable database credentials. Production secrets should be injected through environment/configuration providers; do not save them in `appsettings.json`, scripts, or Dockerfiles.

## Schema changes

```sh
dotnet ef migrations add DescriptiveName --project src/PaidTraffic.Api --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/PaidTraffic.Api
dotnet ef migrations script --idempotent --project src/PaidTraffic.Api --output artifacts/migrations.sql
```

Review generated migrations and run the PostgreSQL tests before delivery. The design-time factory allows schema generation without credentials or connecting to a database. Runtime startup does not silently apply schema changes; use the explicit migration command or reviewed scripts.

## Contributing

Prefer small behavior-complete changes, explicit provider contracts, and tests of business outcomes. Run build, tests, formatting, migration drift checks, and the Compose demo. Keep the project vendor-neutral except where describing the actual external Google Ads integration. Avoid committing build output, local credentials, or test artifacts.
