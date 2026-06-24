# Guides

The Guides section is a progressive tutorial that builds a complete Student CRUD application from scratch. Each guide adds one Schemata capability to the same project, so you can follow along step by step or jump to the guide that covers what you need.

## Prerequisites

- .NET 8 SDK or later
- Basic familiarity with ASP.NET Core and C#
- A text editor or IDE

## Guide Sequence

Start with [Getting Started](getting-started.md) and work through the guides in order. Each guide builds on the code from the previous one. The `Student` entity defined in Getting Started is the running example throughout the series.

| # | Guide | What you add |
| --- | --- | --- |
| 1 | [Getting Started](getting-started.md) | Minimal Student HTTP CRUD API with timestamps and soft-delete |
| 2 | [Unit of Work](unit-of-work.md) | Explicit transaction control for batch mutations |
| 3 | [Object Mapping](object-mapping.md) | Separate request/response DTOs with Mapster |
| 4 | [Concurrency and Freshness](concurrency-and-freshness.md) | Optimistic concurrency, ETags, partial updates |
| 5 | [Filtering and Pagination](filtering-and-pagination.md) | AIP-160 filter, AIP-132 order, cursor pagination |
| 6 | [Query Caching](query-caching.md) | Transparent query result caching with auto-eviction |
| 7 | [Validation](validation.md) | Input validation with FluentValidation |
| 8 | [Identity](identity.md) | User management with ASP.NET Core Identity |
| 9 | [Access Control](access-control.md) | Role-based authorization and row-level security |
| 10 | [Authorization](authorization.md) | OAuth 2.0 / OpenID Connect server |
| 11 | [gRPC Transport](grpc-transport.md) | gRPC endpoints alongside HTTP |
| 12 | [Multi-Tenancy](multi-tenancy.md) | Tenant resolution and data isolation |
| 13 | [Flow](flow.md) | BPMN process engine with typed DSL |
| 14 | [Event Bus](event-bus.md) | In-process event publishing and handling |
| 15 | [Scheduling](scheduling.md) | Cron and periodic background jobs |
| 16 | [Push](push.md) | Broadcast notification fan-out across transports |
| 17 | [Insight](insight.md) | Federated read query across registered sources |
| 18 | [Modular](modular.md) | Module discovery and assembly loading |

## What the guides don't cover

Advanced scenarios — RabbitMQ event bus, flow timer integration, OIDC server setup, custom advisors, and more — live in the [Cookbook](../cookbook/overview.md). Deep mechanism explanations and design rationale live in [Documents](../documents/overview.md).

## Next steps

- [Getting Started](getting-started.md) — minimal Student HTTP CRUD API; the entry point for the chain
- [Unit of Work](unit-of-work.md) — wrap batch mutations in a transaction
- [Identity](identity.md) — jump straight to user management

## See also

- [Cookbook](../cookbook/overview.md) — scenario-driven recipes for advanced use cases
- [Documents](../documents/overview.md) — technical reference for framework internals
