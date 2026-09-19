# 002 — A desired-state provider boundary

## Context

The service must be demonstrable without credentials, while exercising the real failure gap between an external pause and internal persistence.

## Decision

Define `IGoogleAdsClient` for aligned UTC performance snapshots and idempotent pause-to-state commands. Persist simulator state through a separate EF context/commit. Compile a concrete adapter against the official Google Ads .NET SDK, but do not register it in the demo host.

The SDK adapter validates numeric resource IDs and UTC account timezone, converts cost micros to decimal, uses a status-only update mask, disables SDK retries, sets a deadline, and classifies transient transport errors conservatively. The operation ID is internal correlation, not a provider idempotency token.

## Alternatives considered

- Random in-memory simulator: nondeterministic and loses state across restarts.
- Handwritten REST client: duplicates authentication, transport, and generated resource handling.
- Automatically select a live provider when credentials appear: risks unintended account changes.

## Consequences

Automated tests need no Google account. Live activation is an explicit integration change, including campaign catalog ingestion and authenticated configuration. Conversion-value semantics, timezone support beyond UTC, data latency, quotas, and live calls still require account-specific validation. No claim of tested live integration is made.
