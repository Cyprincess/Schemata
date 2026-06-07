# Canonical Name Routing

## What you'll build

A `Book` resource nested under a `Publisher` parent, using the AIP-122 pattern `publishers/{publisher}/books/{book}`. The HTTP route, list filter, and name resolution all derive from the single `[CanonicalName]` attribute declaration.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Resource.Foundation`, `Schemata.Resource.Http`, `Schemata.Common`.

## Step 1: Declare the entity with ICanonicalName and the pattern

Every resource entity must implement `ICanonicalName`. The `[CanonicalName]` attribute on the class tells `ResourceNameDescriptor` how to parse and build names.

```csharp
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;

[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName, IIdentifier, ITimestamp
{
    public string? Publisher    { get; set; }   // parent placeholder
    public string? Name         { get; set; }   // leaf placeholder maps to "Name"
    public string? CanonicalName { get; set; }
    public Guid    Uid          { get; set; }
    public DateTimeOffset? CreateTime { get; set; }
    public DateTimeOffset? UpdateTime { get; set; }
    public string? Title        { get; set; }
}
```

Placeholder resolution rules in `ResourceNameDescriptor`:

- A placeholder whose name matches the entity's singular form (here `book`) maps to the `Name` property.
- A placeholder named `parent` maps to the `Parent` property.
- Any other placeholder maps to a property with the same PascalCase name (here `publisher` maps to `Publisher`).

**Assertion:** `ResourceNameDescriptor.ForType<Book>().Pattern` returns `"publishers/{publisher}/books/{book}"` and `CollectionPath` returns `"publishers/{publisher}/books"`.

## Step 2: Register the resource

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Book>();
```

`ResourceControllerConvention` reads `CollectionPath` from the descriptor and sets the HTTP route template to `~/{package}/publishers/{publisher}/books` (no package prefix when `[ResourcePackage]` is absent). The `{publisher}` segment becomes a route parameter.

**Assertion:** `GET /publishers/acme/books` returns a list of books whose `Publisher` equals `"acme"`. The handler builds a parent predicate from the route value automatically.

## Step 3: Understand how names are resolved

`AdviceAddCanonicalName<T>` runs during the add pipeline. It calls `ResourceNameDescriptor.Resolve(entity)`, which walks the pattern segments and reads the matching properties:

```
publishers/{publisher}/books/{book}
           ^                  ^
           entity.Publisher   entity.Name
```

The result is stored in `entity.CanonicalName`. If `entity.Name` is empty at that point, `Resolve` throws a `ValidationException` with field `name` and reason `not_empty`.

**Assertion:** `POST /publishers/acme/books` with body `{ "title": "Les Misérables", "name": "les-miserables" }` returns a detail whose `canonical_name` is `"publishers/acme/books/les-miserables"`.

## Step 4: Parse a canonical name back to its parts

Use `ResourceNameDescriptor.ParseCanonicalName` when you need to extract parent values and the leaf name from a full canonical name string:

```csharp
var descriptor = ResourceNameDescriptor.ForType<Book>();
var result = descriptor.ParseCanonicalName("publishers/acme/books/les-miserables");
// result.ParentValues = { "publisher": "acme" }
// result.LeafName     = "les-miserables"
```

**Assertion:** `ParseCanonicalName` returns non-null for a well-formed name and `null` for a name that doesn't match the segment count.

## Step 5: Support AIP-159 read-across-collections

To allow `GET /publishers/-/books` (wildcard parent), add `[ReadAcross]` to the entity:

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
[ReadAcross]
public class Book : ICanonicalName, IIdentifier, ITimestamp { ... }
```

`ResourceNameDescriptor.SupportsReadAcross` becomes `true`. The handler skips the parent predicate when the `{publisher}` route value is `"-"`, returning books across all publishers.

**Assertion:** `GET /publishers/-/books` returns books from all publishers. `GET /publishers/acme/books` still filters to `acme` only.

## Common pitfalls

- **Placeholder name must match a property.** If the pattern contains `{author}` but the entity has no `Author` property, `Resolve` throws `MissingFieldException` at runtime. Add the property or rename the placeholder.
- **`Name` must be set before the add pipeline runs.** `AdviceAddCanonicalName` reads `entity.Name` to fill the leaf segment. The sanitize advisor (`AdviceCreateRequestSanitize`) clears `Name` and `CanonicalName` from the incoming request, so the client-supplied name in the request body is the source. If the client omits `name`, the advisor throws a validation error.
- **Parent properties are cleared by the sanitize advisor.** `AdviceCreateRequestSanitize` also clears parent-path properties on the request DTO. The handler re-populates them from route values via `SetParentFromRouteValues` before mapping. Do not rely on the client sending `publisher` in the request body.
- **No `[CanonicalName]` means no pattern.** Without the attribute, `ResourceNameDescriptor.Pattern` is `null`, `CollectionPath` is the pluralized type name in lowercase, and `Resolve` throws `InvalidOperationException`. Every resource entity must carry the attribute if you want AIP-122 names.

## See also

- [Resource naming](../documents/resource/resource-naming.md)
- [Adding a Resource](adding-a-resource.md)
- [Resource overview](../documents/resource/overview.md)
