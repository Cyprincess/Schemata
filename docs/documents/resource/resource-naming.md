# Resource Naming

`Schemata.Common.ResourceNameDescriptor` parses and caches AIP-122 resource-name patterns and resolves, parses,
and builds canonical names. Every resource entity implements `ICanonicalName`; a hierarchical resource also
carries a `[CanonicalName("...")]` pattern.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Common` | `ResourceNameDescriptor.cs`, `ResourceWireNameRules.cs` |
| `Schemata.Abstractions` | `Entities/ICanonicalName.cs`, `Entities/CanonicalNameAttribute.cs` |
| `Schemata.Abstractions` | `Resource/ResourcePackageAttribute.cs`, `Resource/ReadAcrossAttribute.cs` |

## `ICanonicalName`

```csharp
public interface ICanonicalName
{
    string? Name          { get; set; }
    string? CanonicalName { get; set; }
}
```

`Name` holds the leaf segment (`"les-miserables"`); `CanonicalName` holds the full path
(`"publishers/acme/books/les-miserables"`). On the wire, `Name` is suppressed and `CanonicalName` serializes as
`name` — see [HTTP Transport](http-transport.md).

## `[CanonicalName]` pattern

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { /* ... */ }
```

`{placeholder}` segments mark variable parts. `ResourceNameDescriptor` resolves each placeholder's CLR property
by Pascalizing its name (Humanizer `Pascalize()`):

- A placeholder whose Pascalized form equals the descriptor's `Singular` maps to the `Name` property.
- A placeholder named `parent` maps to a `Parent` property.
- Every other placeholder maps to a property with the same Pascalized name.

```
publishers/{publisher}/books/{book}
            {publisher} -> "Publisher" property
            {book}      -> "Book" == Singular -> "Name" property
```

Without a `[CanonicalName]` attribute the resource is flat: `Collection` and `CollectionPath` become the
lowercased plural of the type name, and there are no parent segments.

## `ResourceNameDescriptor`

Descriptors are cached per `RuntimeTypeHandle` in a `ConcurrentDictionary`. Get one with
`ResourceNameDescriptor.ForType<T>()` or `ForType(type)`.

### Properties

| Property | Description |
| --- | --- |
| `Pattern` | The full pattern, or `null` when no attribute is present |
| `Singular` | Singular form from `[DisplayName]`, `[Table]`, or the type name (Humanizer `Singularize()`) |
| `Plural` | `Singular.Pluralize()` |
| `Collection` | The last collection segment, e.g. `"books"` |
| `CollectionPath` | Everything up to and including the last collection segment, e.g. `"publishers/{publisher}/books"` — the basis of the HTTP route |
| `Package` | The `[ResourcePackage]` value (route and gRPC service prefix), or `null` |
| `HasParent` | `true` when the pattern has parent segments |
| `SupportsReadAcross` | `true` when the entity has `[ReadAcross]` (AIP-159 opt-in) |

### Methods

| Method | Description |
| --- | --- |
| `Resolve(entity)` | Builds the full canonical name by reading the placeholder properties from an entity |
| `ParseCanonicalName(name)` | Splits a full name into `(ParentValues, LeafName)`; `null` on mismatch |
| `ParseParent(parent)` | Parses a parent path into a placeholder-to-value dictionary |
| `BuildParentPredicate<T>(values)` | Builds a `Where` predicate from parent values, skipping `"-"` (AIP-159) |
| `ResolveParent(routeValues)` | Builds a parent path string from ASP.NET route values |
| `SetParentFromRouteValues(target, routeValues)` | Sets parent properties on a DTO from route values |
| `ClearParentProperties(target)` | Nulls all parent-segment properties (used by `UpdateAsync`) |

## Wire-name rules

`ResourceWireNameRules.Resolve(owner, propertyName, pluralName)` maps a CLR property to its wire field for both
transports:

- `ICanonicalName.Name` returns `null` — the property is suppressed on the wire.
- `ICanonicalName.CanonicalName` returns `name` (AIP-122).
- `IFreshness.EntityTag` returns `etag` (AIP-154).
- `IEntitiesResult<TItem>.Entities` returns the plural collection name of `TItem` (AIP-132).
- Any other property returns its own name; the transport's naming policy (snake_case) applies on top.

`ResolveClr` inverts these aliases for AIP-161 field-mask parsing, so a mask such as `name,etag` targets the same
properties the response serializes them from.

## AIP-159: reading across collections

With `[ReadAcross]`, a parent segment value of `-` is allowed in `ListAsync`. `BuildParentPredicate` skips `-`
segments, so the query is not scoped to one parent. Without `[ReadAcross]`, a `-` parent throws
`ValidationException` (`CrossParentUnsupported`).

## Extension points

- `[ResourcePackage("myapi")]` sets the route prefix and gRPC service-name prefix.
- `[DisplayName("MyEntity")]` or `[Table("my_entities")]` overrides the singular/plural derivation.
- `[ReadAcross]` opts into AIP-159 wildcard-parent support.

## Design rationale

Caching descriptors per `RuntimeTypeHandle` avoids repeated reflection on hot paths. The pattern is declared once
on the entity, so every operation derives names, routes, and parent predicates from a single source.

## Caveats

- `Resolve` throws `ValidationException` (`NotEmpty`) when a placeholder property is null or empty, and
  `MissingFieldException` when the property does not exist on the entity.
- `ParseCanonicalName` returns `null` when the input does not match the pattern's segment count.
- A type name that Humanizer pluralizes incorrectly needs `[DisplayName]` to override `Singular`/`Plural`.

## See also

- [Resource Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
