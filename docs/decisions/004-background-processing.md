# 004 — PeriodicTimer and durable database state

## Context

Evaluation needs a periodic trigger and a manual trigger for operations and demonstrations. There is no requirement for a complex calendar or guaranteed historical scheduling.

## Decision

Use `BackgroundService` with `PeriodicTimer`; default interval is 60 seconds. Both timer and API invoke the same scoped workflow. Recover unfinished actions first, then evaluate the previous complete UTC hour. A PostgreSQL advisory lock coordinates every participating process. Disable the timer in the Compose demo for predictable initial state.

## Alternatives considered

- A scheduler framework: persistent schedules and dashboards are not yet needed.
- External cron: introduces another deployment boundary and still needs coordination.
- An in-process semaphore alone: cannot coordinate replicas.

## Consequences

Timer ticks do not overlap within a worker, and contending workers skip. No in-memory scheduling state is needed for action recovery. Missed historical evaluation windows are not backfilled. Startup recovery happens on the first tick or a manual call, not immediately at process creation. Large batches hold a connection and lock; split work by account when needed.
