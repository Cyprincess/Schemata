# AutoMapper Adapter

`Schemata.Mapping.AutoMapper` wraps AutoMapper behind `ISimpleMapper`. It translates `SchemataMappingOptions` declarations into AutoMapper profile rules at startup and registers the resulting `MapperConfiguration` as a singleton.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Mapping.AutoMapper` | `SimpleMapper.cs`, `AutoMapperConfigurator.cs`, `SchemataBuilderExtensions.cs`, `SchemataMappingBuilderExtensions.cs` |
| `Schemata.Mapping.Foundation` | `SchemataMappingBuilder.cs`, `SimpleMapperHelper.cs` |

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseAutoMapper();
    // or equivalently:
    schema.UseMapping().UseAutoMapper();
});
```

Both forms call `SchemataMappingBuilder.UseAutoMapper()`, which:

1. Registers `SchemataMappingFeature<Schemata.Mapping.AutoMapper.SimpleMapper>` (Priority 460,000,000).
2. Registers `MapperConfiguration` as a singleton, built from `SchemataMappingOptions` via `AutoMapperConfigurator.Configure`.

## AutoMapperConfigurator

`AutoMapperConfigurator.Configure` translates `SchemataMappingOptions.Mappings` into AutoMapper rules:

```csharp
public static IMapperConfigurationExpression Configure(
    IMapperConfigurationExpression config,
    SchemataMappingOptions         options)
```

For each `(SourceType, DestinationType)` pair in `options.Mappings`:

1. Calls `config.CreateMap<TSource, TDestination>()`.
2. Applies `ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null))` — **null source members are skipped**. This is the AutoMapper null-skip rule.
3. Calls `PreserveReferences()` to handle circular references.
4. Applies per-field rules from `IMapping.Invoke(...)`:
   - `ConvertUsing(converter)` for full-type converters.
   - `ForMember(member, opts => opts.MapFrom(expression))` for field mappings.
   - `ForMember(member, opts => opts.Ignore())` for ignored fields.
   - `Condition((s, d) => !shouldIgnore(s, d))` for conditional mappings.

## Null-skip behavior

The `ForAllMembers` null-skip rule means that if a source property is `null`, the corresponding destination property retains its existing value. This is the correct behavior for partial updates (UpdateMask): only non-null source fields overwrite the destination.

Combined with `SimpleMapperHelper.MapWithMask`, the full update flow is:

1. `MapWithMask` snapshots non-masked destination fields.
2. AutoMapper maps all fields, skipping null source values.
3. `MapWithMask` restores non-masked destination fields.

The result: only fields in the mask that are non-null in the source are updated.

## SimpleMapper

```csharp
public sealed class SimpleMapper : ISimpleMapper
{
    private readonly IMapper _mapper;

    public SimpleMapper(MapperConfiguration config) { _mapper = new Mapper(config); }

    public TDestination? Map<TSource, TDestination>(TSource source)
        => _mapper.Map<TSource, TDestination>(source);

    public void Map<TSource, TDestination>(TSource source, TDestination destination)
        => _mapper.Map(source, destination);

    public void Map<TSource, TDestination>(
        TSource source, TDestination destination, IEnumerable<string> fields)
        => SimpleMapperHelper.MapWithMask(source, destination, fields, (s, d) => _mapper.Map(s, d));

    // ... other overloads delegate to _mapper
}
```

## Declaring mappings

```csharp
schema.UseAutoMapper()
      .Map<StudentRequest, Student>()
      .Map<Student, StudentDetail>(map => {
          map.Field(d => d.FullName, s => s.FirstName + " " + s.LastName);
          map.Ignore(d => d.InternalId);
      });
```

`Map<TSource, TDestination>` without a configure action generates a default mapping with the null-skip rule applied to all members.

## Extension points

- Call `SchemataMappingBuilder.Map<TSource, TDestination>(configure)` to declare custom field mappings.
- Implement `IMapping` to define reusable mapping rules that work across engines.

## Design motivation

The null-skip rule (`ForAllMembers` condition) is applied globally rather than per-field because the resource layer's UpdateMask pattern relies on it: a request DTO with null fields should not overwrite existing entity values. Applying it globally avoids the need to annotate every field individually.

## Caveats

- `MapperConfiguration` is a singleton. AutoMapper validates all mappings at startup. A misconfigured mapping (e.g., a missing member) throws at startup, not at first use.
- `PreserveReferences()` adds a small overhead for non-circular object graphs. If performance is critical and your mappings have no circular references, you can disable it by providing a custom `AutoMapperConfigurator`.
- The null-skip rule applies to all members globally. If you need a specific member to be set to `null` when the source is `null`, use `ForMember(d => d.Prop, opts => opts.AllowNull())` in a custom mapping.

## See also

- [Overview](overview.md)
- [Mapster](mapster.md)
- [Guides: Object Mapping](../../guides/object-mapping.md)
