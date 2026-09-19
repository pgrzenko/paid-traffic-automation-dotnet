# Inspecting telemetry

JSON application logs are available without any extra service:

```sh
docker compose logs -f api
```

Look for `CampaignId`, `EvaluationId`, `IncidentId`, `ActionId`, provider result, and total attempts. The log scope includes request trace/span IDs. The incident detail endpoint returns audit entries with a `traceId` for correlation. Normal logs do not contain connection strings, API keys, OAuth tokens, or raw provider responses.

## Optional local OTLP collector

```sh
docker compose -f docker-compose.yml -f docker-compose.observability.yml up --build
```

Trigger evaluation in another terminal, then inspect exported spans and metric values:

```sh
curl -X POST http://localhost:8080/api/evaluations/run
docker compose -f docker-compose.yml -f docker-compose.observability.yml logs collector
```

The overlay exports every five seconds to an internal collector, whose debug exporter prints received telemetry. It does not publish collector ports to the host or introduce a persistent dashboard. Stop the overlay with the same two `-f` arguments.

For an existing backend, set `OTEL_EXPORTER_OTLP_ENDPOINT` and, where needed, `OTEL_EXPORTER_OTLP_PROTOCOL` and standard exporter authentication variables. Keep exporter credentials outside the repository.

## Interpreting signals

- `campaigns.evaluate` measures the workflow, including recovery and provider interaction.
- `campaign.pause` identifies an execution, which can contain several dispatch attempts. The append-only audit is the per-attempt source of truth.
- Counters count committed outcomes, not invocations or raw provider requests. Duplicate evaluations do not increment evaluation/violation counters.
- The `result` tag has two bounded values. Campaign/incident IDs stay in logs and traces, not metric labels, to avoid unbounded cardinality.
- `evaluation_duration` is in seconds and also records failed runs after lock acquisition. A contended run does not start evaluation timing.
- HTTP server instrumentation adds request latency and result information.

Metrics reset with a process restart and are not transactionally committed with PostgreSQL. Use audit queries for reconciliation. Configure external alerts for failed actions, persistent evaluation errors, long-lived pending/executing incidents, and missing expected runs.
