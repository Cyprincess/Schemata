# JSON Serialization

Schemata configures `System.Text.Json` with opinionated defaults that align with [Google AIP conventions](https://google.aip.dev/general). The `SchemataJsonSerializerFeature` applies these settings to three separate options instances -- the global `JsonSerializerOptions`, the minimal-API `JsonOptions`, and (when controllers are enabled) the MVC `JsonOptions` -- so serialization behavior is consistent across all endpoints.

## Default Settings

The feature's internal `Configure` method applies the following to every `JsonSerializerOptions` instance:

| Setting                  | Value                                                      | Effect                                                                                          |
| ------------------------ | ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `MaxDepth`               | `32`                                                       | Limits object graph traversal to 32 levels. Set on the outermost options before other settings. |
| `PropertyNamingPolicy`   | `JsonNamingPolicy.SnakeCaseLower`                          | Property names are written as `snake_case`.                                                     |
| `DictionaryKeyPolicy`    | `JsonNamingPolicy.SnakeCaseLower`                          | Dictionary keys are also written as `snake_case`.                                               |
| `DefaultIgnoreCondition` | `JsonIgnoreCondition.WhenWritingNull`                      | Properties with `null` values are omitted from output.                                          |
| `NumberHandling`         | `JsonNumberHandling.AllowReadingFromString`                | Numbers encoded as JSON strings are accepted during deserialization.                            |
| Enum converter           | `JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)` | Enums serialize as `kebab-case` strings (e.g., `"not-found"`).                                  |
| Number converter         | `JsonStringNumberConverter.Instance`                       | `long` values serialize as strings. See below.                                                  |
| `TypeInfoResolver`       | `PolymorphicTypeResolver.Instance` with a modifier         | Enables polymorphic serialization and renames `IErrorDetail.Type` to `@type`.                   |

The resolver modifier finds the `Type` property on any type that implements `IErrorDetail` and renames it to `@type` (the value of `SchemataConstants.Parameters.Type`). This follows the AIP convention for typed error details where the discriminator field is `@type` rather than the default property name.

## JsonStringNumberConverter

**Problem.** JavaScript's `Number` type is an IEEE 754 double-precision float. Its maximum safe integer is 2^53 - 1 (`Number.MAX_SAFE_INTEGER`, or 9,007,199,254,740,991). A C# `long` can hold values up to 2^63 - 1. When a large `long` value is serialized as a JSON number, JavaScript clients silently lose precision.

**Solution.** `JsonStringNumberConverter` is a `JsonConverter<long>` that writes every `long` value as a JSON string and reads it back from either a string or a number token.

- **Write path:** Calls `writer.WriteStringValue(value.ToString())`, so the value `1234567890123456789` becomes `"1234567890123456789"` in JSON output.
- **Read path:** Accepts both `JsonTokenType.Number` (via `reader.GetInt64()`) and `JsonTokenType.String` (via `long.TryParse`). Throws `JsonException` if the string cannot be parsed.

The converter is a singleton accessed through `JsonStringNumberConverter.Instance`.

## Polymorphic Serialization

### PolymorphicAttribute

`PolymorphicAttribute` (in `Schemata.Abstractions.Json`) marks a class as a polymorphic derived type under a specified base type. It has two properties:

- `BaseType` (required, set via constructor) -- the base type or interface this class is a derived type of.
- `Name` (optional) -- a string type discriminator. When omitted the runtime type itself is used as the discriminator.

The attribute targets classes and does not inherit:

```csharp
[Polymorphic(typeof(IErrorDetail))]
public class BadRequestDetail : IErrorDetail { ... }
```

All built-in error details use this pattern: `BadRequestDetail`, `ErrorInfoDetail`, `PreconditionFailureDetail`, `QuotaFailureDetail`, `RequestInfoDetail`, and `ResourceInfoDetail` are each annotated `[Polymorphic(typeof(IErrorDetail))]`.

### PolymorphicTypeResolver

`PolymorphicTypeResolver` extends `DefaultJsonTypeInfoResolver`. On construction it scans the application domain (via `AppDomainTypeCache`) for every type annotated with `[Polymorphic]`, groups them by their `BaseType`, and caches the mapping.

When `GetTypeInfo` is called for a base type that has registered derived types, it configures the `PolymorphismOptions`:

| Option                                 | Value                                              |
| -------------------------------------- | -------------------------------------------------- |
| `TypeDiscriminatorPropertyName`        | `"$type"`                                          |
| `IgnoreUnrecognizedTypeDiscriminators` | `true`                                             |
| `UnknownDerivedTypeHandling`           | `JsonUnknownDerivedTypeHandling.FailSerialization` |

Each derived type is added to `DerivedTypes`. If the attribute specifies a `Name`, that string is used as the type discriminator value; otherwise the default (type-based) discriminator is used.

The resolver is a singleton accessed through `PolymorphicTypeResolver.Instance`.

## Customizing Serialization

Pass a configuration delegate to `UseJsonSerializer` on the builder:

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseJsonSerializer(options => {
            // Your customizations run after the defaults are applied.
            options.WriteIndented = true;
        });
    });
```

The delegate receives the `JsonSerializerOptions` instance after all default settings have been applied. This uses the `Configurators` mechanism internally -- the feature calls `configurators.PopOrDefault<JsonSerializerOptions>()` to retrieve any registered delegate and invokes it as the final step of configuration. You can add converters, change policies, or override any of the defaults.
