# Overview

The Core subsystem provides the foundation that every Schemata application is built on. It includes the feature composition system, the advisor pipeline execution engine, JSON serialization configuration, and the structured error model.

## Feature System

Applications are assembled from independent **features**, each registering its own services, middleware, and endpoints through the `SchemataBuilder` API. Features declare ordering via `Order` and `Priority` properties, ensuring middleware and services are configured in the correct sequence. See [Feature System](feature-system.md).

## Advice Pipeline

Cross-cutting concerns are handled through ordered **advisor pipelines**. An advisor implements `IAdvisor<T>` and returns one of three results: `Continue` (proceed to next advisor), `Block` (abort the operation), or `Handle` (complete the operation early). The `Schemata.Advice.Generator` source generator automatically creates pipeline runner methods from advisor interfaces. See [Advice Pipeline](advice-pipeline.md).

## JSON Serialization

The framework configures `System.Text.Json` with consistent conventions: `snake_case` property names, `kebab-case` enum values, `long` values serialized as strings for JavaScript precision safety, and polymorphic type support with `$type` discriminators. See [JSON Serialization](json-serialization.md).

## Error Model

Errors follow a structured format inspired by Google AIP-193, with an `ErrorResponse` envelope containing an `ErrorBody` and typed detail objects. Exception types map to specific HTTP status codes and gRPC status codes. See [Error Model](error-model.md).

## Related

- [Built-in Features](built-in-features.md) — complete list of all framework-provided features with their priorities
