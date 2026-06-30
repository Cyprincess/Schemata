# Canonical Name Routing

## What you'll build

A `Book` resource nested under a `Publisher` parent, using the AIP-122 pattern
`publishers/{publisher}/books/{book}`. The HTTP route, list filter, and name resolution all derive from the single
`[CanonicalName]` declaration.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Resource.Foundation`, `Schemata.Resource.Http`, `Schemata.Common`.

## Step 1: Declare the entity with the pattern

```csharp
using Schemata.Abstractions.Entities;

[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName, IIdentifier, ITimestamp
{
    public string?   Publisher     { get; set; }   // parent placeholder
    public string?   Name          { get; set; }   // leaf placeholder -> Name
    public string?   CanonicalName { get; set; }
    public Guid      Uid           { get; set; }
    public DateTime? CreateTime    { get; set; }
    public DateTime? UpdateTime    { get; set; }
    public string?   Title         { get; set; }
}
```

`ResourceNameDescriptor` resolves placeholders by Pascalizing each name:

- A placeholder whose Pascalized form equals the entity's `Singular` (here `book` → `Book`) maps to `Name`.
- A placeholder named `parent` maps to a `Parent` property.
- Any other placeholder maps to a property with the same Pascalized name (`publisher` → `Publisher`).

**Assertion:** `ResourceNameDescriptor.ForType<Book>().Pattern` is `"publishers/{publisher}/books/{book}"` and
`CollectionPath` is `"publishers/{publisher}/books"`.

## Step 2: Register the resource

```csharp
schema.UseResource()
      .MapHttp()
      .Use<Book>();
```

`ResourceControllerConvention` reads `CollectionPath` and sets the HTTP route template to
`~/v1/publishers/{publisher}/books` (a `[ResourcePackage]` prefix would insert `~/v1/{package}/...`). `{publisher}`
becomes a route parameter.

**Assertion:** `GET /v1/publishers/acme/books` returns the books whose `Publisher` equals `"acme"`. `ListAsync`
fills `request.Parent` from the route and `BuildParentPredicate` scopes the query.

## Step 3: Understand how names are resolved

The repository advisor `Schemata.Entity.Repository.Advisors.AdviceAddCanonicalName<T>` runs during the add
pipeline. It calls `ResourceNameDescriptor.Resolve(entity)`, which reads each placeholder's property:

```
publishers/{publisher}/books/{book}
            entity.Publisher    entity.Name
```

The result is stored in `entity.CanonicalName`. If `entity.Name` is empty when `Resolve` runs, it throws
`ValidationException` with field `name` and reason `NOT_EMPTY`.

**Assertion:** `POST /v1/publishers/acme/books` with body `{ "title": "Les Misérables", "name": "les-miserables" }`
returns a detail whose `name` is `"publishers/acme/books/les-miserables"`.

## Step 4: Parse a canonical name back to its parts

```csharp
var descriptor = ResourceNameDescriptor.ForType<Book>();
var result     = descriptor.ParseCanonicalName("publishers/acme/books/les-miserables");
// result.Value.ParentValues = { "publisher": "acme" }
// result.Value.LeafName     = "les-miserables"
```

**Assertion:** `ParseCanonicalName` returns a non-null tuple for a well-formed name and `null` when the segment
count does not match the pattern.

## Step 5: Support AIP-159 reading across collections

To allow `GET /v1/publishers/-/books` (wildcard parent), add `[ReadAcross]`:

```csharp
[CanonicalName("publishers/{publisher}/books/{book}")]
[ReadAcross]
public class Book : ICanonicalName, IIdentifier, ITimestamp { /* ... */ }
```

`SupportsReadAcross` becomes `true`, and `BuildParentPredicate` skips a `-` parent segment, returning books across
all publishers.

**Assertion:** `GET /v1/publishers/-/books` returns books from every publisher; `GET /v1/publishers/acme/books`
still filters to `acme`.

## Common pitfalls

- **A placeholder with no matching property.** A pattern segment `{author}` with no `Author` property makes
  `Resolve` throw `MissingFieldException`. Add the property or rename the placeholder.
- **`Name` must be set before the add pipeline runs.** The create sanitize advisor clears `Name` and
  `CanonicalName` from the request, so populate `Name` in a repository add advisor (see
  [Adding a Resource](adding-a-resource.md)) or send it in the request body.
- **Parent properties are cleared on the request, then re-bound from the route.** The HTTP controller calls
  `SetParentFromRouteValues` before mapping, so `publisher` comes from the URL, not the request body.
- **No `[CanonicalName]` means no pattern.** Without it, `Pattern` is `null`, `CollectionPath` is the lowercased
  plural type name, and `Resolve` throws `InvalidOperationException`.

## See also

- [Resource Naming](../documents/resource/resource-naming.md)
- [Adding a Resource](adding-a-resource.md)
- [Resource Overview](../documents/resource/overview.md)
