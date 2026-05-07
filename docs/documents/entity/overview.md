# Overview

The Entity subsystem defines the trait interfaces that entities implement to declare their capabilities. Each trait activates automatic behavior through built-in advisors in the repository and resource pipelines.

## Trait-Based Design

Entities declare capabilities through small marker interfaces. The framework detects these traits at runtime and applies the corresponding behavior automatically:

- `ITimestamp` → advisor sets `CreateTime` and `UpdateTime`
- `ISoftDelete` → advisor converts deletes to soft-deletes
- `IConcurrency` → advisor manages optimistic concurrency tokens
- `ICanonicalName` → advisor generates resource names

No explicit wiring is needed. Adding a trait interface to an entity activates the behavior.

## Trait Categories

Traits are grouped by package:

**Entity traits** (`Schemata.Abstractions.Entities`) — implemented on the persistent entity class:
`IIdentifier`, `ITimestamp`, `ISoftDelete`, `IConcurrency`, `ICanonicalName`, `IStateful`, `IDescriptive`, `IExpiration`, `ITransition`, `IOwnable`

**Resource traits** (`Schemata.Abstractions.Resource`) — implemented on request/response DTO types:
`IFreshness`, `IUpdateMask`, `IValidation`, `IRequestIdentification`

See [Traits](traits.md) for the complete reference with interface definitions, advisor mappings, and usage patterns.
