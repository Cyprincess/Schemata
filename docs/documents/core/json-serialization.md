# JSON Serialization

`SchemataJsonSerializerFeature` configures `System.Text.Json` with opinionated defaults aligned to
Google AIP conventions. It applies one set of settings to up to three options instances — the
global `JsonSerializerOptions`, the minimal-API `JsonOptions`, and (when controllers are enabled)
the MVC `JsonOptions` — so every endpoint serializes the same way.

## Where the code lives

| Package                   | Key files                                                                  |
| ------------------------- | -------------------------------------------------------------------------- |
| `Schemata.Core`           | `Features/SchemataJsonSerializerFeature.cs`                                |
| `Schemata.Core`           | `Json/JsonStringNumberConverter.cs`, `Json/PolymorphicTypeResolver.cs`     |
| `Schemata.Abstractions`   | `Json/PolymorphicAttribute.cs`, `SchemataConstants.cs` (`Parameters.Type`) |
| `Schemata.Transport.Http` | `SchemataJsonTraits.cs`, `Features/SchemataTransportHttpFeature.cs`        |

Two layers configure JSON. `SchemataJsonSerializerFeature` (`Schemata.Core`, Priority 240M)
installs the base policies — snake_case names, kebab-case enums, long-as-string, null omission.
`SchemataTransportHttpFeature` (`Schemata.Transport.Http`, Priority 410M) layers the
`PolymorphicTypeResolver` and the `SchemataJsonTraits` trait → AIP wire-name rewrites on top.

## Base settings (SchemataJsonSerializerFeature)

`ConfigureServices` pops any user-provided `Action<JsonSerializerOptions>` from `Configurators`,
then registers a `Configure` delegate that applies these defaults and finally invokes the user
delegate:

| Setting                  | Value                                                      | Effect                                                  |
| ------------------------ | ---------------------------------------------------------- | ------------------------------------------------------- |
| `MaxDepth`               | `32`                                                       | Caps object-graph traversal at 32 levels                |
| `PropertyNamingPolicy`   | `JsonNamingPolicy.SnakeCaseLower`                          | Property names written as `snake_case`                  |
| `DictionaryKeyPolicy`    | `JsonNamingPolicy.SnakeCaseLower`                          | Dictionary keys written as `snake_case`                 |
| `NumberHandling`         | `JsonNumberHandling.AllowReadingFromString`                | Accepts numbers encoded as JSON strings on read         |
| `DefaultIgnoreCondition` | `JsonIgnoreCondition.WhenWritingNull`                      | Omits `null` properties from output                     |
| Enum converter           | `JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)` | Enums serialize as `kebab-case` strings (`"not-found"`) |
| Number converter         | `JsonStringNumberConverter.Instance`                       | Writes every `long` as a JSON string                    |

The feature applies `Configure` to `JsonSerializerOptions` and
`Microsoft.AspNetCore.Http.Json.JsonOptions` unconditionally, and to
`Microsoft.AspNetCore.Mvc.JsonOptions` when `SchemataControllersFeature` is registered. The user
delegate runs after the defaults, so it can add converters or override any policy.

## JsonStringNumberConverter

JavaScript's `Number` is an IEEE 754 double; its maximum safe integer is 2^53 − 1
(9,007,199,254,740,991). A C# `long` reaches 2^63 − 1, so a large `long` serialized as a JSON
number loses precision in a JavaScript client. `JsonStringNumberConverter` is a
`JsonConverter<long>` that:

- **Writes** via `writer.WriteStringValue(value.ToString())`, so `1234567890123456789` becomes
  `"1234567890123456789"`.
- **Reads** from both `JsonTokenType.Number` (via `reader.GetInt64()`) and `JsonTokenType.String`
  (via `long.TryParse`), throwing `JsonException` when a string cannot be parsed.

It is a singleton, `JsonStringNumberConverter.Instance`.

## Polymorphic serialization

### PolymorphicAttribute

`Schemata.Abstractions.Json.PolymorphicAttribute` registers a class as a derived type under a base
type:

```csharp
[Polymorphic(typeof(IErrorDetail))]
public class BadRequestDetail : IErrorDetail { ... }
```

- `BaseType` (constructor argument) — the base type or interface this class derives from.
- `Name` (optional) — the discriminator value. When `null`, the runtime type is used.

The built-in error details all carry `[Polymorphic(typeof(IErrorDetail))]`: `BadRequestDetail`,
`ErrorInfoDetail`, `PreconditionFailureDetail`, `QuotaFailureDetail`, `RequestInfoDetail`,
`ResourceInfoDetail`, and others.

### PolymorphicTypeResolver

`PolymorphicTypeResolver` extends `DefaultJsonTypeInfoResolver`. Its constructor scans the
application domain (via `AppDomainTypeCache`) for every `[Polymorphic]` type and groups them by
`BaseType`. `GetTypeInfo` configures `JsonPolymorphismOptions` for any base type with registered
derived types:

| Option                                 | Value                                              |
| -------------------------------------- | -------------------------------------------------- |
| `TypeDiscriminatorPropertyName`        | `"@type"` (`SchemataConstants.Parameters.Type`)    |
| `IgnoreUnrecognizedTypeDiscriminators` | `true`                                             |
| `UnknownDerivedTypeHandling`           | `JsonUnknownDerivedTypeHandling.FailSerialization` |

The discriminator is emitted directly as `@type`; there is no separate rename step. The resolver
is a singleton, `PolymorphicTypeResolver.Instance`, applied by the HTTP transport.

## SchemataJsonTraits

`SchemataJsonTraits.Apply` runs once per options instance from `SchemataTransportHttpFeature`. It
chains a modifier onto the active `TypeInfoResolver` that rewrites trait-driven properties on the
wire:

| Trait                    | Property        | Wire effect                                                                                                                                  |
| ------------------------ | --------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| `ICanonicalName`         | `Name`          | Hidden — the short identifier is server-managed                                                                                              |
| `ICanonicalName`         | `CanonicalName` | Renamed to `name` (AIP-122)                                                                                                                  |
| `IFreshness`             | `EntityTag`     | Renamed to `etag` (AIP-154)                                                                                                                  |
| `IEntitiesResult<TItem>` | `Entities`      | Renamed to the entity plural from `ResourceNameDescriptor.ForType(...).Plural`, then run through the active `PropertyNamingPolicy` (AIP-132) |

The traits layer is HTTP-only. The gRPC transport (`Schemata.Transport.Grpc`) applies equivalent
renames at the protobuf-net level via `SchemataProtoModelConfigurator`.

## Customizing serialization

Pass a delegate to `UseJsonSerializer`; it runs after the base defaults:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseJsonSerializer(options => {
            options.WriteIndented = true;
        });
    });
```

## Extension points

- Add a `JsonConverter<T>` by appending to `options.Converters` in the `UseJsonSerializer`
  delegate.
- Add a polymorphic hierarchy by annotating derived classes with `[Polymorphic(typeof(BaseType))]`;
  `PolymorphicTypeResolver` discovers them at startup.

## Design rationale

Applying one `Configure` delegate to all three options instances means minimal-API endpoints,
controller endpoints, and any code resolving `IOptions<JsonSerializerOptions>` see identical
behavior. Popping the user delegate from `Configurators` consumes it once, so it applies a single
time.

## Caveats

- The long-as-string behavior covers every `long` property, including primary keys. Clients parse
  these as strings.
- `PolymorphicTypeResolver` scans loaded assemblies at construction. A type in an assembly loaded
  afterward is not discovered.
- `PolymorphicTypeResolver` and `SchemataJsonTraits` take effect only when
  `Schemata.Transport.Http` is present; the base `Schemata.Core` feature alone does not apply
  them.

## See also

- [Error Model](error-model.md) — how `IErrorDetail` and `@type` appear in error responses
- [Built-in Features](built-in-features.md) — `SchemataJsonSerializerFeature` (240M)
- [Feature System](feature-system.md) — the `Configurators.PopOrDefault` pattern
