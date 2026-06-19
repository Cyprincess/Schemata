# AutoMapper Adapter

`Schemata.Mapping.AutoMapper` implements `Schemata.Mapping.Skeleton.ISimpleMapper` over AutoMapper.
It translates `SchemataMappingOptions` into AutoMapper rules at startup and registers the resulting
`AutoMapper.MapperConfiguration` as a singleton.

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseAutoMapper();
    // schema.UseMapping().UseAutoMapper() is the same call chain
});
```

`SchemataMappingBuilder.UseAutoMapper()`:

1. Registers `MapperConfiguration` as a singleton, built from `IOptions<SchemataMappingOptions>`
   and an `ILoggerFactory` resolved from DI.
2. Adds `SchemataMappingFeature<Schemata.Mapping.AutoMapper.SimpleMapper>`
   (Priority `Orders.Extension + 60_000_000`).

The configuration is built inside the singleton factory, so AutoMapper sees every mapping declared
before the service provider is built.

## AutoMapperConfigurator

`Schemata.Mapping.AutoMapper.AutoMapperConfigurator.Configure` groups
`SchemataMappingOptions.Mappings` by `(SourceType, DestinationType)` and applies each group through
a generic `CreateMap<TSource, TDestination>()`:

```csharp
public static IMapperConfigurationExpression Configure(
    IMapperConfigurationExpression config,
    SchemataMappingOptions         options)
```

For each type pair:

1. `CreateMap<TSource, TDestination>()`.
2. `ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null))` — a member
   whose source value is null is skipped, so the destination keeps its current value.
3. `PreserveReferences()` for circular graphs.
4. Per-field rules from each `IMapping.Invoke(...)`:
   - `ConvertUsing(converter)` for a whole-object converter (`With`).
   - `ForMember(member, o => o.Ignore())` for `Ignore()`.
   - `ForMember(member, o => o.Condition((s, d) => !predicate(s, d)))` for `Ignore(predicate)`.
   - `ForMember(member, o => o.MapFrom(expression))` for `For(...).From(...)`, with the same
     conditional guard added when both a source and a predicate are present.

## Null handling

The global `ForAllMembers` condition is the engine-level half of the merge contract: a null source
member never overwrites the destination. The framework still wraps `Map(source, destination)` in
`SimpleMapperHelper.MapMerging`, which additionally treats whitespace-only strings as unpopulated
and restores them by snapshot. The two layers combine so that:

- `Map<TSource, TDestination>(source)` produces a fresh object and copies null members.
- `Map(source, destination)` merges; null or blank source members keep the destination value.
- `Map(source, destination, fields)` masks; a masked field is authoritative and can be cleared.

## SimpleMapper

`Schemata.Mapping.AutoMapper.SimpleMapper` wraps an `AutoMapper.Mapper` built from the singleton
`MapperConfiguration`. The fresh-instance and typed overloads delegate straight to the engine; the
in-place overloads delegate through the foundation helpers:

```csharp
public void Map<TSource, TDestination>(TSource source, TDestination destination)
    => SimpleMapperHelper.MapMerging(source, destination, (s, d) => _mapper.Map(s, d));

public void Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields)
    => SimpleMapperHelper.MapWithMask(source, destination, fields, (s, d) => _mapper.Map(s, d));
```

## Declaring mappings

```csharp
schema.UseAutoMapper()
      .Map<StudentRequest, Student>()
      .Map<Student, StudentDetail>(map => {
          map.For(d => d.FullName).From(s => s.FirstName + " " + s.LastName);
          map.For(d => d.InternalId).Ignore();
      });
```

`Map<TSource, TDestination>()` without a configure action registers a type pair whose member-name
matches map automatically, under the global null-skip rule.

## Caveats

- `MapperConfiguration` validates at startup. A field mapping missing its source expression throws
  `InvalidOperationException` from `Map<,>.Compile()` while the singleton is built, not at first map.
- The null-skip condition is global. A field that must copy null source values needs a custom
  `AutoMapperConfigurator` or a direct `MapperConfiguration` rule that overrides the condition.

## See also

- [Mapping overview](overview.md)
- [Mapster adapter](mapster.md)
- [Multi-engine mapping](../../cookbook/multi-engine-mapping.md)
