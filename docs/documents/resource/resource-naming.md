# Resource Naming

`Schemata.Common.ResourceNameDescriptor` parses and caches AIP-122 resource-name patterns and resolves, parses,
and builds canonical names. Every resource entity implements `ICanonicalName`; a hierarchical resource also
carries a `[CanonicalName("...")]` pattern.

## Where the code lives

| Package                 | Key files                                                                 |
| ----------------------- | ------------------------------------------------------------------------- |
| `Schemata.Common`       | `ResourceNameDescriptor.cs`, `ResourceWireNameRules.cs`                   |
| `Schemata.Abstractions` | `Entities/ICanonicalName.cs`, `Entities/CanonicalNameAttribute.cs`        |
| `Schemata.Abstractions` | `Resource/ResourcePackageAttribute.cs`, `Resource/ReadAcrossAttribute.cs` |

## `ICanonicalName`

```csharp
public interface ICanonicalName
{
    string? Name          { get; set; }
    string? CanonicalName { get; set; }
}
```

`Name` holds the leaf segment (`"les-miserables"`); `CanonicalName` holds the AIP-122 relative resource name
(`"publishers/acme/books/les-miserables"`), the form an API uses within its own scope. On the wire, `Name` is suppressed and `CanonicalName` serializes as
`name` — see [HTTP Transport](http-transport.md).

## `[CanonicalName]` pattern

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { /* ... */ }
```

`{placeholder}` segments mark variable parts. `ResourceNameDescriptor` resolves each placeholder's CLR property
by position:

- The last placeholder of an `ICanonicalName` type maps to the `Name` property, whatever the placeholder is
  spelled. `books/{bookId}` and `parents/{parent}` both bind the leaf to `Name`.
- Every earlier placeholder maps to a property with the same Pascalized name (`-` and `_` are word separators).

```
publishers/{publisher}/books/{book}
            {publisher} -> "Publisher" property
            {book}      -> leaf -> "Name" property
```

The pattern is the sole source of the resource's identity. `Singular` reads the leaf placeholder and `Plural`
reads the collection segment as authored, so `people/{person}` yields `Person` / `People`. A pattern that is a
bare placeholder carries no collection segment, so `Plural` pluralizes the singular instead. `[Table]` names the
SQL table only, and `[DisplayName]` carries a human-readable label with no effect on naming.

A type that declares no addressable pattern carries no resource identity. `Singular` and `Plural` throw
`InvalidOperationException`; `Collection` and `CollectionPath` are empty strings, so a type scan that compares
collection segments still runs over such a type; and the descriptor exposes no parent segments. A type
registered as a resource must carry an addressable pattern — see
[Registration invariants](#registration-invariants).

## `ResourceNameDescriptor`

Descriptors are cached per `RuntimeTypeHandle` in a `ConcurrentDictionary`. Get one with
`ResourceNameDescriptor.ForType<T>()` or `ForType(type)`.

### Properties

| Property             | Description                                                                                                                     |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| `Pattern`            | The full pattern, or `null` when no attribute is present                                                                        |
| `Singular`           | Pascalized leaf placeholder, e.g. `"Book"`; throws `InvalidOperationException` when the type declares no addressable pattern     |
| `Plural`             | Pascalized collection segment, e.g. `"Books"`; throws alongside `Singular`; pluralizes the singular when the pattern is a bare placeholder |
| `Collection`         | The last collection segment, e.g. `"books"`; empty when the type declares no pattern or the pattern is a bare placeholder                                       |
| `CollectionPath`     | Everything up to and including the last collection segment, e.g. `"publishers/{publisher}/books"` — the basis of the HTTP route; empty alongside `Collection` |
| `Package`            | The `[ResourcePackage]` value (route and gRPC service prefix), or `null`                                                        |
| `HasParent`          | `true` when the pattern has parent segments                                                                                     |
| `IsAddressable`      | `true` when the pattern ends in a placeholder preceded by a collection literal                                                  |
| `SupportsReadAcross` | `true` when the entity has `[ReadAcross]` (AIP-159 opt-in)                                                                      |

### Methods

| Method                                          | Description                                                                         |
| ----------------------------------------------- | ----------------------------------------------------------------------------------- |
| `Resolve(entity)`                               | Builds the full canonical name by reading the placeholder properties from an entity |
| `ParseCanonicalName(name)`                      | Splits a full name into `(ParentValues, LeafName)`; `null` on mismatch              |
| `ParseParent(parent)`                           | Parses a parent path into a placeholder-to-value dictionary                         |
| `BuildParentPredicate<T>(values)`               | Builds a `Where` predicate from parent values, skipping `"-"` (AIP-159)             |
| `ResolveParent(routeValues)`                    | Builds a parent path string from ASP.NET route values; `null` when the pattern declares no parent segments or any parent placeholder is unbound |
| `SetParentFromRouteValues(target, routeValues)` | Sets a DTO's parent-segment properties, and `IChild.Parent` when the route is complete |
| `ClearParentProperties(target)`                 | Nulls every parent channel — parent-segment properties and `IChild.Parent` (used by `UpdateAsync`) |

## Wire-name rules

`ResourceWireNameRules.Resolve(owner, propertyName, pluralName)` maps a CLR property to its wire field for both
transports:

- `ICanonicalName.Name` returns `null` — the property is suppressed on the wire.
- `ICanonicalName.CanonicalName` returns `name` (AIP-122).
- `IFreshness.EntityTag` returns `etag` (AIP-154).
- `IEntitiesResult<TItem>.Entities` returns the plural collection name of `TItem` (AIP-140). AIP-132 requires the
  repeated resource field but does not prescribe its plural form.
- Any other property returns its own name; the transport's naming policy (snake_case) applies on top.

`ResolveClrName` inverts these aliases for AIP-161 field-mask parsing, so a mask such as `name,etag` targets the same
properties the response serializes them from.

## AIP-159: reading across collections

With `[ReadAcross]`, a parent segment value of `-` is allowed in `ListAsync`. `BuildParentPredicate` skips `-`
segments, so the query is not scoped to one parent. Without `[ReadAcross]`, a `-` parent throws
`ValidationException` (`CROSS_PARENT_UNSUPPORTED`).

## Registration invariants

`SchemataResourceFeature.RegisterResource` rejects an `ICanonicalName` entity whose pattern is absent, ends in a
literal, or has a placeholder where the collection segment belongs. Such a pattern cannot address a row: standard
Get / Update / Delete routes never resolve and `BuildParentPredicate` cannot scope a child collection. The check
runs at startup so the failure surfaces before the first request.

## Extension points

- `[ResourcePackage("myapi")]` sets the route prefix and gRPC service-name prefix.
- `[ReadAcross]` opts into AIP-159 wildcard-parent support.

## Design rationale

Caching descriptors per `RuntimeTypeHandle` avoids repeated reflection on hot paths. The pattern is declared once
on the entity, so every operation derives names, routes, and parent predicates from a single source.

## Caveats

- `Singular` and `Plural` throw `InvalidOperationException` on a type with no addressable pattern. Guard with
  `IsAddressable` before reading either. The error factories in `Schemata.Common.Errors.SchemataResourceErrors`
  do exactly that: an addressable type reports its canonical singular as `ResourceInfoDetail.ResourceType`, any
  other type reports its CLR name, so building an error stays possible for a type that never declared a pattern.
- `Resolve` throws `ValidationException` (`NotEmpty`) when a placeholder property is null or empty, and
  `MissingFieldException` when the property does not exist on the entity.
- `BuildParentPredicate` throws `MissingFieldException` when a non-wildcard parent value addresses a placeholder
  with no matching property. Skipping it would silently widen the query past the requested parent scope.
- `ParseCanonicalName` returns `null` when the input does not match the pattern's segment count.
- Descriptors are keyed by the runtime type and `[CanonicalName]` is not inherited, so a runtime subclass or ORM
  proxy must re-declare the pattern to be addressable.
- Renaming a leaf placeholder or collection segment moves `Singular` / `Plural`, and with them the gRPC service
  and RPC names, the HTTP controller name, the `ResourceType` in error payloads, and the
  `{singular}.{operation}` permission strings checked by `AuthorizeHelper`.

## See also

- [Resource Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
