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
| 2   | [Object Mapping](object-mapping.md)                       | Separate request/response DTOs with Mapster                   |
| 3   | [Concurrency and Freshness](concurrency-and-freshness.md) | Optimistic concurrency, ETags, partial updates                |
| 4   | [Filtering and Pagination](filtering-and-pagination.md)   | List filtering, sorting, and cursor pagination                |
| 5   | [Validation](validation.md)                               | Input validation with FluentValidation                        |
| 6   | [Identity](identity.md)                                   | User management with ASP.NET Core Identity                    |
| 7   | [Access Control](access-control.md)                       | Role-based authorization and row-level security               |
| 8   | [Authorization](authorization.md)                         | OAuth 2.0 / OpenID Connect server                             |
| 9   | [gRPC Transport](grpc-transport.md)                       | gRPC endpoints alongside HTTP                                 |
| 10  | [Multi-Tenancy](multi-tenancy.md)                         | Tenant resolution and data isolation                          |
| 11  | [Workflow](workflow.md)                                   | Enrollment state machine                                      |
| 12  | [Module System](module-system.md)                         | Modular architecture                                          |
