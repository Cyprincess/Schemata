# Resource Naming

`ResourceNameDescriptor` parses and caches AIP-122 resource name patterns, providing methods for resolving, parsing, and building canonical names. Every entity that participates in the resource system must implement `ICanonicalName` and, for hierarchical names, carry a `[CanonicalName("...")]` attribute that declares the pattern.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Common` | `ResourceNameDescriptor.cs` |
| `Schemata.Common` | `SchemataNaming.cs` |
| `Schemata.Abstractions` | `Entities/ICanonicalName.cs` |
| `Schemata.Abstractions` | `Entities/CanonicalNameAttribute.cs` |

## `ICanonicalName`

```csharp
public interface ICanonicalName
{
    string? Name { get; set; }
    string? CanonicalName { get; set; }
}
```

`Name` holds the leaf segment (e.g., `"les-miserables"`). `CanonicalName` holds the full path (e.g., `"publishers/acme/books/les-miserables"`). Both are set by `AdviceAddCanonicalName<TEntity>` at create time using `ResourceNameDescriptor.Resolve`.

## `[CanonicalName]` attribute

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { ... }
```

The pattern uses `{placeholder}` segments for variable parts. The last placeholder maps to `Name`; earlier placeholders map to parent properties by name (after Pascalizing the placeholder). The special placeholder `{parent}` maps to a `Parent` property.

If no `[CanonicalName]` attribute is present, the descriptor derives `Collection` and `CollectionPath` from the type name (pluralized, lowercased) and treats the resource as flat (no parent segments).

## `ResourceNameDescriptor`

Descriptors are cached per `RuntimeTypeHandle` in a `ConcurrentDictionary`. Call `ResourceNameDescriptor.ForType<T>()` or `ResourceNameDescriptor.ForType(type)` to get the cached instance.

### Key properties

| Property | Description |
|---|---|
| `Pattern` | The full pattern string, e.g., `"publishers/{publisher}/books/{book}"`. `null` when no attribute is present. |
| `Singular` | PascalCase singular form, derived from `[DisplayName]`, `[Table]`, or the type name. |
| `Plural` | PascalCase plural form (Humanizer `Pluralize()`). |
| `Collection` | Last collection segment, e.g., `"books"`. Used as the HTTP route collection segment. |
| `CollectionPath` | Everything up to and including the last collection segment, e.g., `"publishers/{publisher}/books"`. Used as the HTTP route template. |
| `Package` | API package from `[ResourcePackage]`, used as route prefix and gRPC service name prefix. |
| `HasParent` | `true` when the pattern has parent placeholder segments. |
| `SupportsReadAcross` | `true` when the entity has `[ReadAcross]` (AIP-159 wildcard parent opt-in). |

### Key methods

| Method | Description |
|---|---|
| `Resolve(entity)` | Builds the full canonical name from an entity instance by substituting placeholder values. |
| `ParseCanonicalName(name)` | Splits a full canonical name into parent values and leaf name. Returns `null` on mismatch. |
| `ParseParent(parent)` | Parses a parent path string into a placeholder-to-value dictionary. |
| `BuildParentPredicate<T>(parentValues)` | Builds a `Where` predicate expression from parent values. Skips `"-"` wildcards (AIP-159). |
| `ResolveParent(routeValues)` | Builds a parent path string from ASP.NET route values. |
| `SetParentFromRouteValues(target, routeValues)` | Sets parent properties on a DTO from route values. |
| `ClearParentProperties(target)` | Sets all parent-segment properties to `null` (used by `UpdateAsync`). |

## Placeholder resolution

Placeholder names are Pascalized via `SchemataNaming.ToClrMemberName` (which calls Humanizer's `Pascalize()`). The special cases are:

- A placeholder whose Pascalized form equals the entity's `Singular` name maps to the `Name` property.
- A placeholder named `parent` maps to the `Parent` property.
- All other placeholders map to a property with the same Pascalized name.

```csharp
// Pattern: "publishers/{publisher}/books/{book}"
// Entity: Book (Singular = "Book")
// Placeholders:
//   {publisher} -> Pascalize("publisher") = "Publisher" -> property "Publisher"
//   {book}      -> Pascalize("book") = "Book" = Singular -> property "Name"
```

## `SchemataNaming`

```csharp
public static string ToWireName(string clrName)    => clrName.Underscore();
public static string ToClrMemberName(string wire)  => wire.Pascalize();
```

Wire names are snake_case (e.g., `"display_name"`). CLR member names are PascalCase (e.g., `"DisplayName"`). These conversions are used throughout the resource system for field mask parsing, filter member resolution, and error field names.

## Flat resources

An entity without `[CanonicalName]` is treated as a flat resource. Its `CollectionPath` is the pluralized, lowercased type name. Routes are `/{collection}` and `/{collection}/{name}`. There are no parent segments.

```csharp
// No [CanonicalName] attribute
public class Student : ICanonicalName { ... }
// CollectionPath = "students"
// Route: GET /students, GET /students/{name}
```

## AIP-159: Reading across collections

If the entity has `[ReadAcross]`, a parent segment value of `"-"` is allowed in `ListAsync`. The `BuildParentPredicate` method skips `"-"` values, so the query is not scoped to a specific parent. Without `[ReadAcross]`, a `"-"` parent throws `ValidationException` with reason `FieldReasons.CrossParentUnsupported`.

## Extension points

- Add `[ResourcePackage("myapi")]` to set the route prefix and gRPC service name prefix.
- Add `[DisplayName("MyEntity")]` or `[Table("my_entities")]` to control the singular/plural names.
- Add `[ReadAcross]` to opt into AIP-159 wildcard parent support.

## Design motivation

Caching descriptors per `RuntimeTypeHandle` avoids repeated reflection on hot paths. The pattern-based approach means the naming contract is declared once on the entity type and automatically enforced by all operations — no per-operation name-building logic is needed.

## Caveats

- `Resolve` throws `ValidationException` if any placeholder property is null or empty. Ensure all parent properties are set before calling `Resolve` (the `AdviceAddCanonicalName` advisor handles this automatically).
- `ParseCanonicalName` returns `null` if the input does not match the pattern. The handler treats this as an invalid name and throws `ValidationException`.
- The `Singular` and `Plural` forms are derived at descriptor construction time. If the type name does not pluralize correctly with Humanizer, use `[DisplayName]` to override.

## See also

- [Resource Overview](overview.md)
- [HTTP Transport](http-transport.md)
- [gRPC Transport](grpc-transport.md)
- [Entity Traits](../entity/traits.md)
