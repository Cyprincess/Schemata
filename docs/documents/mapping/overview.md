# Mapping

The mapping subsystem provides a unified `ISimpleMapper` abstraction over concrete mapping engines (AutoMapper, Mapster, or custom). Application code depends only on `ISimpleMapper`; the engine is swapped by changing a single `UseAutoMapper()` or `UseMapster()` call at startup. `SimpleMapperHelper.MapWithMask` implements the UpdateMask pattern used by the resource layer for partial updates.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Mapping.Skeleton` | `ISimpleMapper.cs`, `SchemataMappingOptions.cs`, `Configurations/Map.cs`, `Configurations/Mapping.cs`, `Configurations/FieldSelection.cs`, `Configurations/IMapping.cs`, `Extensions/ServiceCollectionExtensions.cs`, `Extensions/MapperExtensions.cs` |
| `Schemata.Mapping.Foundation` | `SchemataMappingBuilder.cs`, `SimpleMapperHelper.cs`, `Features/SchemataMappingFeature.cs`, `Extensions/SchemataBuilderExtensions.cs`, `Extensions/SchemataMappingBuilderExtensions.cs` |
| `Schemata.Mapping.AutoMapper` | `SimpleMapper.cs`, `AutoMapperConfigurator.cs`, `SchemataBuilderExtensions.cs`, `SchemataMappingBuilderExtensions.cs` |
| `Schemata.Mapping.Mapster` | `SimpleMapper.cs`, `MapsterConfigurator.cs`, `SchemataBuilderExtensions.cs`, `SchemataMappingBuilderExtensions.cs` |

## Startup

`UseMapping` on `SchemataBuilder` returns a `SchemataMappingBuilder` for selecting the engine. `UseMapping` itself does not register a feature — the engine selection method does:

```csharp
builder.UseSchemata(schema => {
    schema.UseMapping().UseAutoMapper();
    // or
    schema.UseAutoMapper();  // shorthand, same result
});
```

`SchemataMappingFeature<T>` (Priority 460,000,000) registers `ISimpleMapper` as scoped with the concrete implementation `T`.

## ISimpleMapper

```csharp
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

The overload `Map<TSource, TDestination>(source, destination, fields)` maps only the specified fields from source onto the destination, preserving other destination values. This is the UpdateMask overload used by the resource layer.

## SimpleMapperHelper.MapWithMask

`MapWithMask` implements the snapshot-and-restore pattern for field-selective mapping:

```csharp
public static void MapWithMask<TSource, TDestination>(
    TSource                       source,
    TDestination                  destination,
    IEnumerable<string>           mask,
    Action<TSource, TDestination> mapAction)
```

1. Builds a `HashSet<string>` from `mask` (case-insensitive).
2. Snapshots the current values of all writable properties on `destination` that are **not** in the mask.
3. Calls `mapAction(source, destination)` — the full mapping runs.
4. Restores the snapshotted values for non-masked properties.

This means the mapping engine runs its full logic (including null-skip rules), but non-masked fields are always restored to their pre-map values. Both `AutoMapper.SimpleMapper` and `Mapster.SimpleMapper` delegate their `Map(..., fields)` overload to `MapWithMask`.

## Mapping configuration

Mappings are declared via `SchemataMappingBuilder.Map<TSource, TDestination>`:

```csharp
schema.UseAutoMapper()
      .Map<StudentRequest, Student>()
      .Map<Student, StudentDetail>(map => {
          map.Field(d => d.FullName, s => s.FirstName + " " + s.LastName);
      });
```

`Map<TSource, TDestination>` registers an `IMapping` via `SchemataMappingOptions`. The configurator (`AutoMapperConfigurator` or `MapsterConfigurator`) translates these into engine-specific rules at startup.

## Feature priority

| Feature | Priority |
| --- | --- |
| `SchemataMappingFeature<T>` | 440,000,000 |

## Extension points

- Implement `ISimpleMapper` to wrap a different mapping engine.
- Implement `IMapping` to define custom mapping rules.
- Use `SchemataMappingBuilder.Map<TSource, TDestination>` to declare mappings without engine-specific code.

## Design motivation

The `ISimpleMapper` abstraction decouples application code from the mapping engine. The `MapWithMask` pattern is necessary because neither AutoMapper nor Mapster natively supports "map only these fields and leave the rest unchanged" — both engines map all fields by default. The snapshot-and-restore approach is engine-agnostic and works correctly with both null-skip rules.

## Caveats

- `MapWithMask` uses reflection to snapshot and restore property values. For large objects with many properties, this has a small performance cost. The resource layer calls it only on update operations, not on reads.
- `SchemataMappingFeature<T>` uses `TryAddScoped`, so the first registered engine wins. If both `UseAutoMapper()` and `UseMapster()` are called, only the first takes effect.

## See also

- [AutoMapper](automapper.md)
- [Mapster](mapster.md)
- [Guides: Object Mapping](../../guides/object-mapping.md)
