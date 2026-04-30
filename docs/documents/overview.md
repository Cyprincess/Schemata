# Overview

The Documents section provides technical reference for every subsystem in the Schemata framework. It is organized by domain area, with each section describing the architecture, interfaces, configuration, and behavior of a specific part of the framework.

## Sections

### Core

The foundation that all other features build on.

| Document                                         | Description                                                      |
| ------------------------------------------------ | ---------------------------------------------------------------- |
| [Feature System](core/feature-system.md)         | SchemataBuilder, FeatureBase, feature lifecycle, custom features |
| [Advice Pipeline](core/advice-pipeline.md)       | IAdvisor, AdviceContext, AdviseResult, pipeline execution        |
| [Built-in Features](core/built-in-features.md)   | All built-in and extension features with priorities              |
| [JSON Serialization](core/json-serialization.md) | snake_case, long-as-string, polymorphic types                    |
| [Error Model](core/error-model.md)               | Exception hierarchy, structured error responses                  |

### Entity

The entity modeling layer.

| Document                   | Description                              |
| -------------------------- | ---------------------------------------- |
| [Traits](entity/traits.md) | All trait interfaces and their behaviors |

### Repository

The data access layer.

| Document                                             | Description                                        |
| ---------------------------------------------------- | -------------------------------------------------- |
| [Overview](repository/overview.md)                   | IRepository API, suppression methods, unit of work |
| [Mutation Pipeline](repository/mutation-pipeline.md) | Add/Update/Remove advisor chains                   |
| [Query Pipeline](repository/query-pipeline.md)       | BuildQuery/Query/Result advisors                   |
| [Caching](repository/caching.md)                     | Query and result caching                           |
| [Providers](repository/providers.md)                 | EF Core and LinqToDB setup                         |

### Resource

The CRUD service layer and transport bindings.

| Document                                       | Description                                  |
| ---------------------------------------------- | -------------------------------------------- |
| [Overview](resource/overview.md)               | ResourceOperationHandler, type parameters    |
| [Create Pipeline](resource/create-pipeline.md) | Create operation, idempotency                |
| [Read Pipeline](resource/read-pipeline.md)     | List and Get operations                      |
| [Update Pipeline](resource/update-pipeline.md) | Field masks, concurrency, freshness          |
| [Delete Pipeline](resource/delete-pipeline.md) | Soft-delete, physical delete                 |
| [Resource Naming](resource/resource-naming.md) | Canonical names, resource name patterns      |
| [HTTP Transport](resource/http-transport.md)   | REST endpoint generation                     |
| [gRPC Transport](resource/grpc-transport.md)   | gRPC service generation                      |
| [Filtering](resource/filtering.md)             | AIP-160 filter grammar, ordering, pagination |

### Feature-Specific

| Document                          | Description                             |
| --------------------------------- | --------------------------------------- |
| [Mapping](mapping.md)             | ISimpleMapper, AutoMapper, Mapster      |
| [Validation](validation.md)       | FluentValidation integration            |
| [Security](security.md)           | Access providers, entitlement providers |
| [Identity](identity.md)           | ASP.NET Core Identity                   |
| [Authorization](authorization.md) | OAuth 2.0 / OIDC                        |
| [Tenancy](tenancy.md)             | Multi-tenant resolution                 |
| [Workflow](workflow.md)           | State machine orchestration             |
| [Modules](modules.md)             | Module discovery and lifecycle          |
| [Packages](packages.md)           | Meta-package matrix                     |
