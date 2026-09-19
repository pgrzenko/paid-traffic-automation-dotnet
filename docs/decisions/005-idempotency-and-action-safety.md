# 005 — Durable intent and reconciliation, with explicit limits

## Context

An external pause can succeed while the client times out or the next database write fails. No transaction spans PostgreSQL and Google Ads.

## Decision

Use four controls together:

1. Unique campaign/policy/window evaluation, unique incident per evaluation, and unique action per incident.
2. A partial unique index allowing only one unresolved incident per campaign.
3. A session advisory lock across participating workflow operations, retained through intermediate commits.
4. Save `Executing` intent and every dispatch attempt before the provider call; reconcile unfinished intents by setting the desired paused state again.

Completed and rejected records are not reopened. Exhausted and permanent failures wait for explicit retry. Audit is append-only in application code and database triggers.

## Alternatives considered

- Mark completion before dispatch: loses work after a crash.
- Dispatch before saving intent: creates untraceable effects.
- Claim exactly-once execution from a database transaction: cannot cover an external API.
- Queue/outbox with leases: useful at scale but still requires idempotent effects and reconciliation.

## Consequences

At-least-once dispatch can repeat a harmless desired-state pause. It is not a general solution for non-idempotent financial operations. A lost lock connection during an in-flight provider call can permit another process to reconcile concurrently; provider state-setting mitigates this but does not fence outside actors. Manual re-enabling during recovery can be overwritten. Database administrators can bypass audit triggers. See the failure matrix in `docs/reliability.md`.
