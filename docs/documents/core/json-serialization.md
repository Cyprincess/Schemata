# JSON Serialization

`SchemataJsonSerializerFeature` configures `System.Text.Json` with opinionated defaults that align with Google AIP conventions. It applies the same settings to three separate options instances — the global `JsonSerializerOptions`, the minimal-API `JsonOptions`, and (when controllers are enabled) the MVC `JsonOptions` — so serialization behavior is consistent across all endpoints.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Core` | `Features/SchemataJsonSerializerFeature.cs` |
| `Schemata.Core` | `Json/JsonStringNumberConverter.cs`, `Json/PolymorphicTypeResolver.cs` |
| `Schemata.Transport.Http` | `Features/SchemataTransportHttpFeature.cs`, `SchemataJsonTraits.cs` |
| `Schemata.Abstractions` | `Json/PolymorphicAttribute.cs`, `SchemataConstants.cs` (Parameters.*) |
| `Schemata.Abstractions` | `Errors/IErrorDetail.cs` |

JSON configuration applies in two layers. `SchemataJsonSerializerFeature` (`Schemata.Core`, Priority 240M) installs the base policies — snake_case, kebab-case enums, long-as-string, polymorphic resolution, `@type` rename for `IErrorDetail`. `SchemataTransportHttpFeature` (`Schemata.Transport.Http`, Priority 410M) layers `SchemataJsonTraits` on top via `PostConfigure` to perform the Schemata trait → AIP wire-name rewrites described below.

## Default settings

`SchemataJsonSerializerFeature.ConfigureServices` calls `configurators.PopOrDefault<JsonSerializerOptions>()` to retrieve any user-provided delegate, then applies the following defaults before invoking it:

| Setting | Value | Effect |
| --- | --- | --- |
| `MaxDepth` | `32` | Limits object graph traversal to 32 levels |
| `PropertyNamingPolicy` | `JsonNamingPolicy.SnakeCaseLower` | Property names are written as `snake_case` |
| `DictionaryKeyPolicy` | `JsonNamingPolicy.SnakeCaseLower` | Dictionary keys are also written as `snake_case` |
| `DefaultIgnoreCondition` | `JsonIgnoreCondition.WhenWritingNull` | Properties with `null` values are omitted from output |
| `NumberHandling` | `JsonNumberHandling.AllowReadingFromString` | Numbers encoded as JSON strings are accepted during deserialization |
| Enum converter | `JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)` | Enums serialize as `kebab-case` strings (e.g. `"not-found"`) |
| Number converter | `JsonStringNumberConverter.Instance` | `long` values serialize as strings (see below) |
| `TypeInfoResolver` | `PolymorphicTypeResolver.Instance` with a modifier | Enables polymorphic serialization; renames `IErrorDetail.Type` to `@type` |

The resolver modifier finds the `Type` property on any type that implements `IErrorDetail` and renames it to `@type` (the value of `SchemataConstants.Parameters.Type`). This follows the AIP convention for typed error details.

The feature applies these settings to `JsonSerializerOptions`, `Microsoft.AspNetCore.Http.Json.JsonOptions`, and (when `SchemataControllersFeature` is registered) `Microsoft.AspNetCore.Mvc.JsonOptions`.

## JsonStringNumberConverter

JavaScript's `Number` type is an IEEE 754 double-precision float. Its maximum safe integer is 2^53 - 1 (9,007,199,254,740,991). A C# `long` can hold values up to 2^63 - 1. When a large `long` is serialized as a JSON number, JavaScript clients silently lose precision.

`JsonStringNumberConverter` is a `JsonConverter<long>` that writes every `long` value as a JSON string and reads it back from either a string or a number token:

- **Write path:** calls `writer.WriteStringValue(value.ToString())`, so `1234567890123456789` becomes `"1234567890123456789"` in JSON output.
- **Read path:** accepts both `JsonTokenType.Number` (via `reader.GetInt64()`) and `JsonTokenType.String` (via `long.TryParse`). Throws `JsonException` if the string cannot be parsed.

The converter is a singleton accessed through `JsonStringNumberConverter.Instance`.

## Polymorphic serialization

### PolymorphicAttribute

`PolymorphicAttribute` marks a class as a polymorphic derived type under a specified base type:

```csharp
[Polymorphic(typeof(IErrorDetail))]
public class BadRequestDetail : IErrorDetail { ... }
```

Properties:

- `BaseType` (required, set via constructor) — the base type or interface this class is a derived type of.
- `Name` (optional) — a string type discriminator. When omitted the runtime type itself is used.

All built-in error details use this pattern: `BadRequestDetail`, `ErrorInfoDetail`, `PreconditionFailureDetail`, `QuotaFailureDetail`, `RequestInfoDetail`, and `ResourceInfoDetail` are each annotated `[Polymorphic(typeof(IErrorDetail))]`.

### PolymorphicTypeResolver

`PolymorphicTypeResolver` extends `DefaultJsonTypeInfoResolver`. On construction it scans the application domain (via `AppDomainTypeCache`) for every type annotated with `[Polymorphic]`, groups them by `BaseType`, and caches the mapping.

When `GetTypeInfo` is called for a base type that has registered derived types, it configures `PolymorphismOptions`:

| Option | Value |
| --- | --- |
| `TypeDiscriminatorPropertyName` | `"$type"` |
| `IgnoreUnrecognizedTypeDiscriminators` | `true` |
| `UnknownDerivedTypeHandling` | `JsonUnknownDerivedTypeHandling.FailSerialization` |

The resolver is a singleton accessed through `PolymorphicTypeResolver.Instance`.

## SchemataJsonTraits

`SchemataJsonTraits.Apply` is invoked once by `SchemataTransportHttpFeature` for each of `JsonSerializerOptions`, `Microsoft.AspNetCore.Http.Json.JsonOptions`, and `Microsoft.AspNetCore.Mvc.JsonOptions`. It chains a modifier onto the existing `TypeInfoResolver` that rewrites three trait-driven properties on the wire:

| Trait | Property | Wire effect |
| --- | --- | --- |
| `ICanonicalName` | `Name` | Hidden — the short identifier is server-managed, not part of the public surface |
| `ICanonicalName` | `CanonicalName` | Renamed to `name` (AIP-122) |
| `IFreshness` | `EntityTag` | Renamed to `etag` (AIP-154) |
| `ListResultBase<TSummary>` | `Entities` | Renamed to the entity plural resolved via `ResourceNameDescriptor.ForType(summary).Plural`, then mapped through the active `PropertyNamingPolicy` (AIP-132) |

The traits layer is HTTP-only. The gRPC transport (`Schemata.Transport.Grpc`) applies the equivalent renames at the protobuf-net level via `SchemataProtoTraits`.

## Customizing serialization

Pass a configuration delegate to `UseJsonSerializer`:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseJsonSerializer(options => {
            // Runs after the defaults are applied.
            options.WriteIndented = true;
        });
    });
```

The delegate receives the `JsonSerializerOptions` instance after all default settings have been applied. You can add converters, change policies, or override any of the defaults.

## Extension points

- Add a custom `JsonConverter<T>` by appending to `options.Converters` in the `UseJsonSerializer` delegate.
- Add a new polymorphic type hierarchy by annotating derived classes with `[Polymorphic(typeof(BaseType))]`. The resolver picks them up automatically at startup.
- Replace the `TypeInfoResolver` entirely by assigning `options.TypeInfoResolver` in the delegate (runs after the default resolver is set).

## Design motivation

Applying the same settings to all three options instances (`JsonSerializerOptions`, `JsonOptions`, `MvcJsonOptions`) ensures that minimal-API endpoints, controller endpoints, and any code that resolves `IOptions<JsonSerializerOptions>` directly all see the same behavior. The `PopOrDefault` pattern lets the feature consume the user delegate exactly once, preventing double-application.

## Caveats

- The `long`-as-string behavior applies to all `long` properties, including entity primary keys and timestamps. Clients must parse these as strings, not numbers.
- `PolymorphicTypeResolver` scans `AppDomain.CurrentDomain.GetAssemblies()` at construction time. Types in assemblies loaded after the resolver is constructed will not be discovered.
- The `$type` discriminator is set by `PolymorphicTypeResolver`. The `@type` rename applies only to `IErrorDetail` implementations and is applied via the `WithAddedModifier` callback in `SchemataJsonSerializerFeature`.

## See also

- [Error Model](error-model.md) — how `IErrorDetail` and `@type` are used in error responses
- [Built-in Features](built-in-features.md) — `SchemataJsonSerializerFeature` priority (240M)
- [Feature System](feature-system.md) — `Configurators.PopOrDefault` pattern
