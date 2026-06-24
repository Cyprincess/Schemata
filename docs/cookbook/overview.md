# Cookbook

The Cookbook contains scenario-driven, end-to-end recipes for advanced use cases. Each recipe is self-contained and copy-pasteable. Where a recipe builds on the Student example from the guides, it says so explicitly.

Recipes are organized by domain. Pick the one that matches your scenario and follow it from top to bottom.

## Messaging and Event-Driven

| Recipe | What it covers |
| --- | --- |
| [RabbitMQ Event Bus](rabbitmq-event-bus.md) | Producer, consumer, DLX, request/reply with RabbitMQ |
| [Domain Events](domain-events.md) | Publish from a committed repository advisor |
| [Push Notifications](push-notifications.md) | A custom transport, a send advisor, and durable scheduled delivery |

## Scheduling and Flow Integration

| Recipe | What it covers |
| --- | --- |
| [Cron Jobs](cron-jobs.md) | A cron schedule with Cronos syntax and a missed-fire policy |
| [Flow with Timers](flow-with-timers.md) | A BPMN intermediate timer catch fired through the scheduler |
| [Flow with Events](flow-with-events.md) | A BPMN event-based gateway correlated through the event bus |

## Identity, Security, and Tenancy

| Recipe | What it covers |
| --- | --- |
| [OIDC Server](oidc-server.md) | Full authorization code + PKCE, scopes, applications |
| [Ownership and Row ACL](ownership-and-row-acl.md) | UseOwner, IOwnable, per-owner query filtering |
| [Multi-Tenant Setup](multi-tenant-cookbook.md) | Header + path + claim resolvers combined, per-tenant EF connection |

## Data and Resources

| Recipe | What it covers |
| --- | --- |
| [Soft Delete and Recovery](soft-delete-and-recovery.md) | ISoftDelete, SuppressQuerySoftDelete, restore endpoint |
| [Canonical Name Routing](canonical-name-routing.md) | [CanonicalName] attribute, placeholder resolution |
| [Adding a Resource](adding-a-resource.md) | [Resource] attribute, MapHttp, MapGrpc |
| [Distributed Cache](distributed-cache.md) | Adding Redis, eviction strategy trade-offs |
| [Custom Advisor](custom-advisor.md) | Author a per-entity advisor, register via TryAddEnumerable, integration test |
| [Custom Expression Language](custom-expression-language.md) | Implement IExpressionCompiler, keyed DI registration |
| [Federated Query](federated-query.md) | Join repository sources, compute aggregates, and drill into nested rows |

## Mapping

| Recipe | What it covers |
| --- | --- |
| [Multi-Engine Mapping](multi-engine-mapping.md) | Switching AutoMapper vs Mapster, UpdateMask field set |

## Modular

| Recipe | What it covers |
| --- | --- |
| [Module Packaging](module-packaging.md) | Ship a feature as its own assembly so a host application picks it up from a single package or project reference |

## Recipe format

Every recipe follows this structure:

1. **What you'll build** — one paragraph describing the end state
2. **Prerequisites** — packages, prior guides, or running services required
3. **Steps** — sequential, each ending with a verifiable assertion
4. **Common pitfalls** — known failure modes and how to avoid them
5. **See also** — links to related documents and guides

## See also

- [Guides](../guides/overview.md) — step-by-step tutorials for core capabilities
- [Documents](../documents/overview.md) — technical reference for framework internals
