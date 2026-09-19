# 003 — Authority is an explicit policy choice

## Context

Some campaigns may tolerate immediate intervention; others require an operator to review the evidence. Silent defaults or repeated approvals can change budget allocation unexpectedly.

## Decision

Each immutable policy selects `Automatic` or `RequireApproval`. A violation creates one incident and pause action. Automatic incidents enter `Executing`; approval-required incidents enter `PendingApproval`. Only pending incidents can be approved or rejected. Failed actions require an explicit retry endpoint. Every operator decision is audited.

## Alternatives considered

- Automatically pause every violation: no review path for sensitive campaigns.
- Always require approval: preserves the operational delay the product is intended to reduce.
- Infer mode from spend: conceals an important product choice inside code.

## Consequences

Operators can reconstruct the authority behind a pause. Duplicate approvals return 409. Approval applies to the recorded snapshot and currently does not expire. Rejection is final for that snapshot/window, not a permanent exemption. Production needs named principals, role checks, approval expiry, and a decision on revalidation of stale evidence.
