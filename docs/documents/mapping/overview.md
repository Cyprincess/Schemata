# Mapping

The mapping subsystem exposes a single `Schemata.Mapping.Skeleton.ISimpleMapper` abstraction over
a concrete engine (AutoMapper or Mapster). Application code depends only on `ISimpleMapper`; the
engine is chosen by one `UseAutoMapper()` or `UseMapster()` call at startup. Two helpers in
`Schemata.Mapping.Foundation.SimpleMapperHelper` implement the AIP-134 update semantics the
resource layer relies on: `MapMerging` for implicit-mask merges and `MapWithMask` for explicit
field masks.

## Where the code lives

| Package | Role | Key types |
| --- | --- | --- |
| `Schemata.Mapping.Skeleton` | Contracts | `ISimpleMapper`, `SchemataMappingOptions`, `Configurations.Map<,>`, `MaskTree`, `MaskWalker` |
| `Schemata.Mapping.Foundation` | Builder + helpers | `SchemataMappingBuilder`, `SimpleMapperHelper`, `SchemataMappingFeature<T>` |
| `Schemata.Mapping.AutoMapper` | AutoMapper adapter | `SimpleMapper`, `AutoMapperConfigurator` |
| `Schemata.Mapping.Mapster` | Mapster adapter | `SimpleMapper`, `MapsterConfigurator` |

## Startup

`UseMapping()` on `SchemataBuilder` returns a `SchemataMappingBuilder`. The engine method
registers the feature; `UseMapping()` alone registers nothing.

```csharp
builder.UseSchemata(schema => {
    schema.UseMapping().UseAutoMapper();
    // schema.UseAutoMapper() is the same call chain
});
```

`UseAutoMapper()` and `UseMapster()` each register their engine configuration as a singleton and
add `SchemataMappingFeature<SimpleMapper>`. The feature's `ConfigureServices` registers
`ISimpleMapper` with `TryAddScoped`, so the first engine added wins.

## ISimpleMapper

```csharp
namespace Schemata.Mapping.Skeleton;

public interface ISimpleMapper
{
    T? Map<T>(object source);
    T? Map<T>(object source, Type sourceType, Type destinationType);
    TDestination? Map<TSource, TDestination>(TSource source);
    void Map<TSource, TDestination>(TSource source, TDestination destination);
    void Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields);
    object? Map(object source, Type sourceType, Type destinationType);
    void Map(object source, object destination, Type sourceType, Type destinationType);
}
```

Three families of overload, each with distinct null behavior:

| Overload | Semantics | Null source member |
| --- | --- | --- |
| `Map<T>(source)`, `Map<TSource, TDestination>(source)` | Produce a fresh instance | Copied to the result |
| `Map(source, destination)` | Merge onto an existing object | Preserves the destination value |
| `Map(source, destination, fields)` | Field-selective update | Masked fields are authoritative; unmasked preserved |

Both adapters route the two-argument `Map(source, destination)` through
`SimpleMapperHelper.MapMerging` and the field overload through `SimpleMapperHelper.MapWithMask`.
The fresh-instance overloads call the engine directly, so they copy null source members verbatim.

`MapperExtensions.Each` and `EachAsync` (in `Schemata.Mapping.Skeleton`) map a sequence element
by element and drop null results.

## Merge semantics

`MapMerging` gives `Map(source, destination)` AIP-134 implicit-mask behavior: a source member that
is null or whitespace-only is treated as unpopulated, and its destination value is kept.

```text
source.Nickname = null  ->  destination.Nickname unchanged
source.Nickname = "  "  ->  destination.Nickname unchanged
source.Nickname = "New" ->  destination.Nickname = "New"
```

`MapMerging` snapshots every destination property whose matching source property is unpopulated,
runs the engine map, then restores the snapshots. The engine's own null handling never has to be
relied upon for this guarantee.

## Field-mask semantics

`MapWithMask` implements explicit AIP-161 field masks. The mask is a set of CLR property paths
(dot paths traverse nested objects); listed fields are written authoritatively, and everything
else is restored to its pre-map value.

```csharp
public static void MapWithMask<TSource, TDestination>(
    TSource                       source,
    TDestination                  destination,
    IEnumerable<string>           mask,
    Action<TSource, TDestination> mapAction)
```

1. Parse the mask into a `MaskTree` rooted at `typeof(TDestination)` via `MaskTree.FromClr`.
2. Snapshot the value of every writable destination property outside the mask, recursing into
   masked interior objects through `MaskWalker.WalkUnmasked`.
3. Run `mapAction(source, destination)` — the engine maps the whole object.
4. Restore the snapshots, then copy the masked leaf values from source so a masked field can be
   set to null.

A masked leaf is authoritative: if the source value is null, the destination leaf is cleared. An
unmasked field always retains its pre-map value, even one the engine would otherwise have written.

The mask uses CLR property names. The resource layer converts wire-format (`snake_case`) mask
paths to CLR leaf paths with `MaskTree.FromWire` before calling this overload; see
[Object Mapping](../../guides/object-mapping.md).

`MaskTree` validates every path segment against the destination type through reflection, so an
unknown or collection-traversing path raises `ArgumentException` at parse time.

## Declaring mappings

`SchemataMappingBuilder.Map<TSource, TDestination>` and the `IServiceCollection.Map<,>` extension
both append to `SchemataMappingOptions`. A configure action uses the
`Schemata.Mapping.Skeleton.Configurations.Map<TSource, TDestination>` fluent surface:

```csharp
schema.UseAutoMapper()
      .Map<StudentRequest, Student>()
      .Map<Student, StudentDetail>(map => {
          map.For(d => d.FullName).From(s => s.FirstName + " " + s.LastName);
          map.For(d => d.InternalId).Ignore();
      });
```

| Method | Effect |
| --- | --- |
| `For(dest)` | Begin a field mapping for the destination property |
| `From(src)` | Supply the source expression for the current field |
| `Ignore()` | Skip the field |
| `Ignore((s, d) => ...)` | Skip the field when the predicate holds |
| `With(s => dest)` | Whole-object converter for the type pair |

`SchemataMappingOptions.AddMapping` invokes the configure action, then calls `Map<,>.Compile()`.
`Compile()` throws `InvalidOperationException` ("Mapping for field {0} is missing a source field.")
for any field that has neither a source expression, an ignore, nor a converter.

## Feature ordering

| Feature | Priority |
| --- | --- |
| `SchemataMappingFeature<T>` | `Orders.Extension + 60_000_000` (460,000,000) |

## Extension points

- Implement `ISimpleMapper` to bridge a different engine, then register it through a feature.
- Implement `Schemata.Mapping.Skeleton.Configurations.IMapping` to define rules an existing
  configurator can translate.
- Resolve `MapperConfiguration` (AutoMapper) or `TypeAdapterConfig` (Mapster) from DI to add
  engine-specific rules the fluent surface does not express.

## See also

- [AutoMapper adapter](automapper.md)
- [Mapster adapter](mapster.md)
- [Object Mapping guide](../../guides/object-mapping.md)
