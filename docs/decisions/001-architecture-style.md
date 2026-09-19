# 001 — A modular monolith with a small pure domain

## Context

Campaign monitoring, incident decisions, and pause execution form one operational workflow. They share consistency requirements and do not yet need independent scaling.

## Decision

Use one ASP.NET Core API/worker and PostgreSQL. Keep deterministic rules and legal state transitions in a dependency-free domain project. Group infrastructure and use cases by purpose inside the host. Use EF Core directly rather than adding generic repositories or a mediator.

## Alternatives considered

- Separate monitoring and action microservices: requires messaging and distributed operations before there is a throughput need.
- One flat project: workable, but a tiny domain library makes rule purity enforceable.
- A full layered architecture framework: adds interfaces and indirection without a corresponding use case.

## Consequences

Deployment and transaction boundaries remain easy to inspect. The workflow deliberately serializes work across instances. Account partitioning and durable dispatch can be extracted when measurements justify them. The domain does not depend on EF, HTTP, or Google libraries.
