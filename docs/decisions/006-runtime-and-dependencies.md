# 006 — .NET 10 LTS and reproducible package resolution

## Context

The initial local inspection found no installed .NET SDK. This repository needs a supported LTS baseline and compatible EF/provider packages rather than relying on an incidental workstation installation.

## Decision

Install and use SDK 10.0.401; target `net10.0`. Pin the SDK with patch roll-forward, use .NET/ASP.NET/EF 10.0.12, and select Npgsql EF 10.0.3. Commit NuGet lock files and restore in locked mode in CI and container builds. Pin the EF tool in a repository-local tool manifest.

Explicitly reference EF Relational 10.0.12 so its runtime version flows to consumers of the API project; a private tooling reference alone does not do that. Use Polly only for provider retries and OpenTelemetry only for diagnostics. Include the Google Ads SDK because a concrete adapter consumes it.

## Alternatives considered

- .NET 8 LTS: near the end of its support window at the time of implementation.
- Preview runtimes: no production benefit here.
- Floating package versions: makes clean builds change without a source change.

## Consequences

The dependency graph is repeatable. Regular patch updates still require lock-file regeneration and complete CI verification. Container tags fix .NET patch versions; PostgreSQL uses the 17 Alpine line and should be pinned by digest in controlled production release pipelines.

Sources checked during implementation: [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Npgsql 10 release notes](https://www.npgsql.org/efcore/release-notes/10.0.html), [official Google Ads SDK releases](https://github.com/googleads/google-ads-dotnet/releases), and NuGet package metadata. .NET 10 LTS is supported through November 2028.
