# Resource Naming

The resource system follows AIP-122 resource name patterns for identifying entities. Resource names are hierarchical paths like `publishers/acme/books/les-miserables` that encode both the resource's identity and its parent relationships.

## ICanonicalName

Every type used in the resource system must implement `ICanonicalName`:

```csharp
public interface ICanonicalName
{
    string? Name { get; set; }
    string? CanonicalName { get; set; }
}
```

| Property        | Description                                                                                 |
| --------------- | ------------------------------------------------------------------------------------------- |
| `Name`          | The short resource name -- the leaf segment of the canonical name (e.g., `les-miserables`). |
| `CanonicalName` | The fully-qualified resource name (e.g., `publishers/acme/books/les-miserables`).           |

The `Name` property is used for entity lookups in the database. The `CanonicalName` is a computed value used primarily by the gRPC transport for entity resolution.

## CanonicalNameAttribute

The `[CanonicalName]` attribute declares the AIP-122 resource name pattern for an entity type:

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { ... }
```

The pattern consists of alternating collection segments (literal strings) and placeholder segments (in curly braces). The pattern is parsed by `ResourceNameDescriptor` at startup.

### Pattern Rules

| Segment     | Example                 | Description                                                    |
| ----------- | ----------------------- | -------------------------------------------------------------- |
| Collection  | `publishers`, `books`   | A literal lowercase plural noun identifying the resource type. |
| Placeholder | `{publisher}`, `{book}` | A variable that maps to an entity property.                    |

Placeholder-to-property mapping follows these rules:

1. If the PascalCase form of the placeholder equals `"Parent"`, it maps to the `Parent` property.
2. If the PascalCase form matches the entity's singular name (derived from `DisplayNameAttribute`, `TableAttribute`, or the type name), it maps to the `Name` property.
3. Otherwise, it maps to `{PascalCase}Name` (e.g., `{publisher}` maps to `PublisherName`).

### Examples

| Pattern                                    | Leaf maps to | Parent maps to        |
| ------------------------------------------ | ------------ | --------------------- |
| `books/{book}`                             | `Name`       | (none)                |
| `publishers/{publisher}/books/{book}`      | `Name`       | `PublisherName`       |
| `orgs/{org}/teams/{team}/members/{member}` | `Name`       | `OrgName`, `TeamName` |

## ResourceNameDescriptor

`ResourceNameDescriptor` is the runtime representation of a parsed `[CanonicalName]` pattern. It is cached per entity type via `ResourceNameDescriptor.ForType<T>()` or `ResourceNameDescriptor.ForType(type)`.

### Key Properties

| Property             | Type      | Description                                                                                                               |
| -------------------- | --------- | ------------------------------------------------------------------------------------------------------------------------- |
| `Pattern`            | `string?` | The raw pattern from `[CanonicalName]`, or null if no attribute.                                                          |
| `Singular`           | `string`  | PascalCase singular name (from `DisplayNameAttribute`, `TableAttribute`, or type name).                                   |
| `Plural`             | `string`  | PascalCase plural (via Humanizer).                                                                                        |
| `Collection`         | `string`  | The last collection segment from the pattern (e.g., `books`).                                                             |
| `CollectionPath`     | `string`  | Everything up to and including the last collection segment. Used for HTTP routing (e.g., `publishers/{publisher}/books`). |
| `Package`            | `string?` | The API package prefix from `[ResourcePackage]`.                                                                          |
| `HasParent`          | `bool`    | `true` when the pattern has parent placeholder segments.                                                                  |
| `SupportsReadAcross` | `bool`    | `true` when `[ReadAcross]` is present on the entity type.                                                                 |

### Key Methods

| Method                                          | Description                                                                        |
| ----------------------------------------------- | ---------------------------------------------------------------------------------- |
| `Resolve(entity)`                               | Resolves an entity instance to its full canonical name by reading property values. |
| `ParseCanonicalName(name)`                      | Parses a full canonical name into parent values and the leaf name.                 |
| `ParseParent(parent)`                           | Parses a parent string into placeholder-to-value mappings.                         |
| `ResolveParent(routeValues)`                    | Builds a parent string from ASP.NET route values.                                  |
| `ExtractParentValues(routeValues)`              | Extracts parent placeholder values from route values as a dictionary.              |
| `BuildParentPredicate<T>(parentValues)`         | Builds a LINQ `Where` expression from parent values, skipping `"-"` wildcards.     |
| `SetParentFromRouteValues(target, routeValues)` | Sets parent properties on an object from route values.                             |
| `ClearParentProperties(target)`                 | Sets all parent properties to null (used during update to prevent overwriting).    |

## ResourcePackageAttribute

`[ResourcePackage]` specifies an API package prefix for a resource:

```csharp
[ResourcePackage("api/v1")]
[CanonicalName("books/{book}")]
public class Book : ICanonicalName { ... }
```

The package affects both transports:

- **HTTP**: The route becomes `~/api/v1/books` instead of `~/books`.
- **gRPC**: The service name becomes `api.v1.BookService` instead of `BookService`.

When no `[ResourcePackage]` is present, `Package` is null and no prefix is applied.

## ReadAcrossAttribute

`[ReadAcross]` opts a resource into AIP-159 read-across behavior:

```csharp
[ReadAcross]
[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName { ... }
```

When present, clients can use the wildcard parent `"-"` to list resources across parent boundaries:

```
GET /publishers/-/books?filter=genre="fiction"
```

Without `[ReadAcross]`, a parent value of `"-"` throws an `InvalidArgumentException` with the reason `CrossParentUnsupported`.

When `"-"` is used for a parent segment, `BuildParentPredicate` skips that segment, effectively removing the parent scoping from the query.

## How Names Flow Through Operations

### Create

1. `request.Name` and `request.CanonicalName` are cleared (set to null).
2. If `IIdentifier` is implemented, `request.Id` is reset to 0.
3. The request is mapped to an entity.
4. Parent properties are populated from HTTP route values via `SetParentFromRouteValues`.
5. The canonical name is computed after persistence by `AdviceAddCanonicalName` (an `IRepositoryAddAdvisor<TEntity>` implementation) using `ResourceNameDescriptor.Resolve`.

### Update

1. The entity is resolved by name before the update pipeline starts.
2. After advisors run, `request.Name`, `request.CanonicalName`, and parent properties are all cleared.
3. If `IIdentifier` is implemented, `request.Id` is reset.
4. The request is mapped onto the entity with cleared identity/parent fields, so existing values are preserved.

### Get (HTTP)

The entity is resolved via `GetByNameAsync(name, http, ct)`, which extracts parent values from the HTTP route and queries by both name and parent predicates.

### Get (gRPC)

The entity is resolved via `GetByCanonicalNameAsync(request.CanonicalName, ct)`, which parses the full canonical name into parent values and leaf name, then queries.

### Delete

The entity is resolved by name the same way as Get.
