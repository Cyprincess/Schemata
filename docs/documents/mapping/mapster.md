# Mapster Adapter

`Schemata.Mapping.Mapster` wraps Mapster behind `ISimpleMapper`. It translates `SchemataMappingOptions` declarations into Mapster `TypeAdapterConfig` rules at startup and registers the resulting configuration as a singleton.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Mapping.Mapster` | `SimpleMapper.cs`, `MapsterConfigurator.cs`, `SchemataBuilderExtensions.cs`, `SchemataMappingBuilderExtensions.cs` |
| `Schemata.Mapping.Foundation` | `SchemataMappingBuilder.cs`, `SimpleMapperHelper.cs` |

## Activation

```csharp
builder.UseSchemata(schema => {
    schema.UseMapster();
    // or equivalently:
    schema.UseMapping().UseMapster();
});
```

Both forms call `SchemataMappingBuilder.UseMapster()`, which:

1. Registers `SchemataMappingFeature<Schemata.Mapping.Mapster.SimpleMapper>` (Priority 460,000,000).
2. Registers `TypeAdapterConfig` as a singleton, built from `SchemataMappingOptions` via `MapsterConfigurator.Configure`.

## MapsterConfigurator

`MapsterConfigurator.Configure` translates `SchemataMappingOptions.Mappings` into Mapster rules:

```csharp
public static TypeAdapterConfig Configure(
    TypeAdapterConfig     config,
    SchemataMappingOptions options)
```

For each `(SourceType, DestinationType)` pair in `options.Mappings`:

1. Calls `config.NewConfig<TSource, TDestination>()`.
2. Applies per-field rules from `IMapping.Invoke(...)`:
   - `MapWith(converter)` for full-type converters.
   - `Map(member, expression)` for field mappings.
   - `Ignore(member)` for ignored fields.
   - `IgnoreIf(predicate, member)` for conditional mappings.

Unlike the AutoMapper adapter, `MapsterConfigurator` does not apply a global null-skip rule via the configurator. Mapster's null-value behavior is controlled by `IgnoreNullValues(true)` on the `TypeAdapterConfig`, which is set by `SchemataMappingBuilderExtensions.UseMapster()`.

## IgnoreNullValues behavior

`UseMapster()` calls `config.Default.Settings.IgnoreNullValues(true)` on the global `TypeAdapterConfig`. This means null source members are skipped globally, matching the AutoMapper null-skip behavior.

Combined with `SimpleMapperHelper.MapWithMask`, the full update flow is:

1. `MapWithMask` snapshots non-masked destination fields.
2. Mapster maps all fields, skipping null source values (due to `IgnoreNullValues(true)`).
3. `MapWithMask` restores non-masked destination fields.

The result: only fields in the mask that are non-null in the source are updated.

## SimpleMapper

```csharp
public sealed class SimpleMapper : ISimpleMapper
{
    private readonly Mapper _mapper;

    public SimpleMapper(TypeAdapterConfig config) { _mapper = new(config); }

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
schema.UseMapster()
      .Map<StudentRequest, Student>()
      .Map<Student, StudentDetail>(map => {
          map.Field(d => d.FullName, s => s.FirstName + " " + s.LastName);
          map.Ignore(d => d.InternalId);
      });
```

`Map<TSource, TDestination>` without a configure action generates a default mapping with `IgnoreNullValues(true)` applied globally.

## Switching from AutoMapper to Mapster

Replace `UseAutoMapper()` with `UseMapster()`. No other changes are needed if you use only the `ISimpleMapper` interface and `SchemataMappingBuilder.Map` declarations. Engine-specific behavior differences:

| Behavior | AutoMapper | Mapster |
| --- | --- | --- |
| Null source members | Skipped (`ForAllMembers` condition) | Skipped (`IgnoreNullValues(true)`) |
| Circular references | `PreserveReferences()` | Not configured by default |
| Startup validation | Validates all mappings at startup | Lazy validation on first use |
| Performance | Compiled expression trees | Compiled expression trees |

## Extension points

- Call `SchemataMappingBuilder.Map<TSource, TDestination>(configure)` to declare custom field mappings.
- Access `TypeAdapterConfig` directly via DI to add Mapster-specific rules not expressible through `IMapping`.

## Design motivation

`IgnoreNullValues(true)` is applied globally rather than per-mapping because the resource layer's UpdateMask pattern relies on it. Applying it globally avoids the need to annotate every mapping individually and ensures consistent behavior across all mappings in the application.

## Caveats

- `IgnoreNullValues(true)` is a global setting on `TypeAdapterConfig.Default.Settings`. If you need a specific mapping to copy null values, use `config.NewConfig<TSource, TDestination>().IgnoreNullValues(false)` after `MapsterConfigurator.Configure` runs.
- Mapster validates mappings lazily (on first use) rather than at startup. Misconfigured mappings may not surface until the first request that exercises them.
- `TypeAdapterConfig` is a singleton. Mapster is not thread-safe for configuration mutations after the first use. Do not modify `TypeAdapterConfig` after startup.

## See also

- [Overview](overview.md)
- [AutoMapper](automapper.md)
- [Guides: Object Mapping](../../guides/object-mapping.md)
