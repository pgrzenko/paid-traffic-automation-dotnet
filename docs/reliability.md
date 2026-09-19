# Reliability contract

## Failure matrix

| Failure point | Durable state | Recovery |
|---|---|---|
| Before saving an evaluation | No decision or effect | Next run reevaluates |
| After evaluation and action intent commit | `Executing` + action | Next run reconciles before reading campaigns |
| Before a dispatch attempt is saved | No new provider call | Existing intent remains recoverable |
| Transient provider failure | Attempt remains in audit | Up to two bounded retries |
| Permanent provider failure | `ExecutionFailed` + error category | Operator repairs cause, then retries |
| Retries exhausted | `ExecutionFailed` | No timer-driven retry storm |
| Provider success, completion persistence fails | Provider paused, intent still `Executing` | Next run sees already paused and commits completion |
| Cancellation or process crash during call | Outcome may be unknown | Reconcile desired state on next run |
| Duplicate evaluation in the same hour | Unique evaluation identity exists | Skip |
| Concurrent workflow | Session lock occupied | API returns 409, worker skips |
| New hour while an unresolved incident exists | New evaluation, original incident retained | No second pending/failed action |
| Rejected incident in a new hour | New evidence | May create another incident |

`SaveChangesAsync` commits related evidence, intent, and audit rows atomically. The session lock intentionally spans several short transactions, rather than holding a database transaction open around network calls. Database execution retries are not enabled because transparently replacing the connection would undermine the session lock.

The pause resilience pipeline has at most three calls per execution, delays of 200 ms and 400 ms, and a five-second timeout per attempt. These timeouts require provider cancellation cooperation; they are not a mechanism for killing arbitrary code. The SDK adapter also sets a four-second RPC deadline and disables internal SDK retries. Querying state plus mutating it can exceed a single RPC deadline, but the outer pause timeout bounds the whole attempt. The simulator respects cancellation through EF calls. Authorization, invalid resources, and unclassified provider errors are permanent. Resource exhaustion is conservatively operator-visible rather than blindly retried across account quota limits.

## Deliberate boundaries

- **No exactly-once claim.** A pause is a desired-state operation. The same action can be dispatched again after ambiguity. Google Ads does not use the internal operation UUID as a deduplication key.
- **Lock loss is not fencing.** If the lock connection dies while a provider call continues, another instance can acquire the lock. Two calls may then set the same desired state. External manual re-enabling can be overwritten during recovery. Strict multi-actor coordination needs a broader operating contract.
- **One policy per campaign.** Policies are immutable through the API. Versioning and modification are future work; do not edit persisted policies in place because evaluation identity includes policy ID, not its contents.
- **One previous complete UTC hour.** No arbitrary date-window API, rolling aggregation, missed-hour backfill, or data correction processing is implemented. The simulator supplies fixed scenario values for each requested hour. Real reporting latency and attribution corrections make immediate automated action unsuitable until a business-specific delay and revalidation policy is defined.
- **Approval has no expiry.** A pending decision may become stale. The operator is approving recorded evidence, not a fresh query. Production should revalidate and expire approvals.
- **Read-side failure.** A snapshot fetch failure fails that run. The worker retries on a later tick; already recorded intents are recovered first. There is no shared circuit breaker because this low-volume, serialized workflow has bounded dispatch and no measured need for one.
- **Audit protection is operational.** Triggers reject update/delete/truncate, including direct SQL, but privileged administrators can disable them. Use a separate restricted runtime role and an external immutable archive for stronger guarantees.
- **No automatic rollback of a pause.** Undoing a pause may spend money. There is no hidden compensating re-enable action.
- **No savings estimate.** Historical spend and a pause do not establish counterfactual money saved.

## Operating a failure

Inspect `/api/incidents/{id}`, its source evaluation, action attempts, and ordered audit. Correlate the trace ID in JSON logs or the trace backend. Repair configuration or provider access before calling `/retry`. A response reporting `ExecutionFailed` is an operational failure even when the HTTP decision endpoint returned 200.

Alert on failed actions, old pending approvals, old `Executing` intents, sustained evaluation failures, and absence of successful evaluation runs. Back up PostgreSQL and test restore. The demo's migration container is single-use and privileged; production migrations should run once as a separately authorized deployment job.
