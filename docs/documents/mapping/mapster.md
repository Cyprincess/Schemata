# Mapster Adapter

`Schemata.Mapping.Mapster` implements `Schemata.Mapping.Skeleton.ISimpleMapper` over Mapster. It
translates `SchemataMappingOptions` into a `Mapster.TypeAdapterConfig` at startup and registers
that configuration as a singleton.

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseMapster();
    // schema.UseMapping().UseMapster() is the same call chain
});
```

`SchemataMappingBuilder.UseMapster()`:

1. Registers `TypeAdapterConfig` as a singleton. The factory clones
   `TypeAdapterConfig.GlobalSettings`, applies `Default.IgnoreNullValues(true).PreserveReference(true)`,
   then runs `MapsterConfigurator.Configure`.
2. Adds `SchemataMappingFeature<Schemata.Mapping.Mapster.SimpleMapper>`
   (Priority `Orders.Extension + 60_000_000`).

The configuration is a clone of the global settings, so the process-wide `TypeAdapterConfig` stays
untouched.

## MapsterConfigurator

`Schemata.Mapping.Mapster.MapsterConfigurator.Configure` groups `SchemataMappingOptions.Mappings`
by `(SourceType, DestinationType)` and applies each group through `NewConfig<TSource, TDestination>()`:

```csharp
public static TypeAdapterConfig Configure(
    TypeAdapterConfig      config,
    SchemataMappingOptions options)
```

Per-field rules from each `IMapping.Invoke(...)`:

- `MapWith(converter)` for a whole-object converter (`With`).
- `Ignore(member)` for `Ignore()`.
- `IgnoreIf(predicate, member)` for `Ignore(predicate)`.
- `Map(member, expression)` for `For(...).From(...)`.

## Null handling

`Default.IgnoreNullValues(true)` is the engine-level half of the merge contract: a null source
member is skipped, so the destination keeps its value. `Default.PreserveReference(true)` handles
circular graphs. The framework still wraps `Map(source, destination)` in
`SimpleMapperHelper.MapMerging`, which additionally treats whitespace-only strings as unpopulated.
The result matches the AutoMapper adapter:

- `Map<TSource, TDestination>(source)` produces a fresh object and copies null members.
- `Map(source, destination)` merges; null or blank source members keep the destination value.
- `Map(source, destination, fields)` masks; a masked field is authoritative and can be cleared.

## SimpleMapper

`Schemata.Mapping.Mapster.SimpleMapper` wraps a `MapsterMapper.Mapper` built from the singleton
`TypeAdapterConfig`. The fresh-instance and typed overloads delegate straight to the engine; the
in-place overloads delegate through the foundation helpers:

```csharp
public void Map<TSource, TDestination>(TSource source, TDestination destination)
    => SimpleMapperHelper.MapMerging(source, destination, (s, d) => _mapper.Map(s, d));

public void Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields)
    => SimpleMapperHelper.MapWithMask(source, destination, fields, (s, d) => _mapper.Map(s, d));
```

## Declaring mappings

```csharp
schema.UseMapster()
      .Map<StudentRequest, Student>()
      .Map<Student, StudentDetail>(map => {
          map.For(d => d.FullName).From(s => s.FirstName + " " + s.LastName);
          map.For(d => d.Nickname).Ignore((s, _) => s.Name == "Hidden");
      });
```

`Map<TSource, TDestination>()` without a configure action registers a type pair whose member-name
matches map automatically, under the global `IgnoreNullValues(true)`.

## Caveats

- `TypeAdapterConfig` is built once as a singleton. The fluent surface still validates field
  mappings at startup: `Map<,>.Compile()` throws when a non-ignored, non-converter field lacks a
  source expression.
- A field mapping that must copy null source values needs `config.NewConfig<TSource, TDestination>()
.IgnoreNullValues(false)`, applied to the resolved `TypeAdapterConfig`.

## See also

- [Mapping overview](overview.md)
- [AutoMapper adapter](automapper.md)
- [Multi-engine mapping](../../cookbook/multi-engine-mapping.md)
