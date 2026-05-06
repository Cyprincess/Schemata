# Overview

The Guides section is a progressive tutorial that builds a complete Student CRUD application from scratch. Each guide adds one Schemata capability to the same project, so you can follow along step by step.

## Prerequisites

- .NET 8 SDK or later
- Basic familiarity with ASP.NET Core and C#
- A text editor or IDE

## Guide Sequence

Start with [Getting Started](getting-started.md) and work through the guides in order. Each guide builds on the code from the previous one.

| #   | Guide                                                     | What You Add                                                  |
| --- | --------------------------------------------------------- | ------------------------------------------------------------- |
| 1   | [Getting Started](getting-started.md)                     | Minimal Student HTTP CRUD API with timestamps and soft-delete |
| 2   | [Unit of Work](unit-of-work.md)                           | Explicit transaction control for batch mutations              |
| 3   | [Object Mapping](object-mapping.md)                       | Separate request/response DTOs with Mapster                   |
| 4   | [Concurrency and Freshness](concurrency-and-freshness.md) | Optimistic concurrency, ETags, partial updates                |
| 5   | [Filtering and Pagination](filtering-and-pagination.md)   | List filtering, sorting, and cursor pagination                |
| 6   | [Query Caching](query-caching.md)                         | Transparent query result caching with auto-eviction           |
| 7   | [Validation](validation.md)                               | Input validation with FluentValidation                        |
| 8   | [Identity](identity.md)                                   | User management with ASP.NET Core Identity                    |
| 9   | [Access Control](access-control.md)                       | Role-based authorization and row-level security               |
| 10  | [Authorization](authorization.md)                         | OAuth 2.0 / OpenID Connect server                             |
| 11  | [gRPC Transport](grpc-transport.md)                       | gRPC endpoints alongside HTTP                                 |
| 12  | [Multi-Tenancy](multi-tenancy.md)                         | Tenant resolution and data isolation                          |
| 13  | [Flow — Process Engine](workflow.md)                        | BPMN process engine with typed DSL                               |
| 14  | [Module System](module-system.md)                         | Modular architecture                                          |
