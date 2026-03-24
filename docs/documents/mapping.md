# Mapping

The mapping subsystem provides an engine-agnostic abstraction for object-to-object mapping. Application code depends on the `ISimpleMapper` interface, while the concrete mapping engine (Mapster or AutoMapper) is selected at startup through a builder extension method. Mapping configurations are expressed through a fluent API that compiles down to engine-specific rules, so switching engines requires no changes to mapping definitions.

## ISimpleMapper

`ISimpleMapper` is the core abstraction, registered as a scoped service. It defines seven method overloads organized into three groups: creating new instances, populating existing instances, and working with runtime types.

```csharp
public interface ISimpleMapper
{
    // Create a new instance from an untyped source
    T? Map<T>(object source);

    // Create a new instance, casting the result to T, using explicit runtime types
    T? Map<T>(object source, Type sourceType, Type destinationType);

    // Create a new TDestination from a strongly-typed source
    TDestination? Map<TSource, TDestination>(TSource source);

    // Map onto an existing destination (full overwrite of mapped properties)
    void Map<TSource, TDestination>(TSource source, TDestination destination);

    // Map onto an existing destination, updating only the named fields
    void Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields);

    // Create a new instance using runtime types
    object? Map(object source, Type sourceType, Type destinationType);

    // Map onto an existing destination using runtime types
    void Map(object source, object destination, Type sourceType, Type destinationType);
}
```

The first three overloads return a new object (or `null` on failure). The fourth and fifth overloads mutate an existing destination in place. The last two overloads accept `Type` arguments for scenarios where the types are not known at compile time.

## Field-level mapping

The fifth overload -- `Map<TSource, TDestination>(TSource source, TDestination destination, IEnumerable<string> fields)` -- performs a selective update. Only properties whose names appear in the `fields` enumerable are written to the destination; all other properties retain their pre-existing values.

Both the Mapster and AutoMapper implementations delegate to `SimpleMapperHelper.MapWithMask`, which works as follows:

1. Build a case-insensitive `HashSet<string>` from the field names.
2. Snapshot the values of every writable destination property that is **not** in the set.
3. Invoke the underlying engine's full map (which overwrites all mapped properties).
4. Restore the snapshotted values, effectively reverting any properties that were not in the mask.

Writable properties are resolved through `AppDomainTypeCache.GetWritableProperties`, which returns all public properties where both `CanRead` and `CanWrite` are `true`.

### Integration with IUpdateMask

`IUpdateMask` is an interface in `Schemata.Abstractions.Resource` that request DTOs can implement to support partial updates:

```csharp
public interface IUpdateMask
{
    string? UpdateMask { get; set; }
}
```

The `UpdateMask` property holds a comma-separated list of field paths (in lower_snake_case). When `ResourceOperationHandler` processes an update, it checks whether the request implements `IUpdateMask`:

```csharp
if (request is IUpdateMask { UpdateMask: { } mask }) {
    var properties = AppDomainTypeCache.GetProperties(typeof(TEntity));
    var fields     = mask.Split(',')
                         .Select(f => f.Trim().Pascalize())
                         .Where(f => properties.ContainsKey(f));
    _mapper.Map(request, entity, fields);
} else {
    _mapper.Map(request, entity);
}
```

The handler splits the mask on commas, pascalizes each segment (converting `display_name` to `DisplayName`), validates that each name corresponds to an actual entity property, and passes the filtered set to the field-level `Map` overload. If no `UpdateMask` is present, the handler falls through to the full map, overwriting all mapped properties.

## Mapping configuration

### SchemataMappingOptions

`SchemataMappingOptions` accumulates mapping definitions. Each call to `AddMapping<TSource, TDestination>` creates a `Map<TSource, TDestination>` instance, invokes the optional configuration lambda, compiles the result into a list of `IMapping` objects, and appends them:

```csharp
options.AddMapping<Order, OrderDto>(map => {
    map.For(d => d.Total).From(s => s.ComputedTotal);
    map.For(d => d.InternalNotes).Ignore();
});
```

### Fluent API

The `Map<TSource, TDestination>` class provides two entry points:

- **`For(destination)`** -- begins a field mapping by selecting the destination property. Returns a `FieldSelection<TSource, TDestination>`.
- **`With(converter)`** -- registers a whole-object converter expression instead of field-by-field mapping.

`FieldSelection<TSource, TDestination>` provides:

- **`From(source)`** -- specifies the source property expression for the current field.
- **`Ignore(condition?)`** -- marks the destination field as ignored, optionally gated by a predicate. When called without a condition, the field is unconditionally ignored. When called with a condition `(source, destination) => bool`, the field is ignored only when the predicate returns `true`.
- **`For(destination)`** -- chains into the next field mapping.

These calls can be chained fluently:

```csharp
map.For(d => d.FullName).From(s => s.Name)
   .For(d => d.Secret).Ignore()
   .For(d => d.Status).From(s => s.State)
                      .Ignore((s, d) => s.State == null);
```

When `Compile()` runs, it validates that every non-converter, non-ignored mapping has a source field expression configured. Missing source fields throw an `InvalidOperationException`.

### Registering mappings

Mappings can be registered at two levels.

**On the service collection directly** (via an extension method on `IServiceCollection`):

```csharp
services.Map<Order, OrderDto>(map => {
    map.For(d => d.Total).From(s => s.ComputedTotal);
});
```

**On the mapping builder** (via an extension method on `SchemataMappingBuilder`):

```csharp
builder.UseSchemata(schema => {
    schema.UseMapster()
          .Map<Order, OrderDto>()
          .Map<Order, OrderSummary>(map => {
              map.For(d => d.Label).From(s => s.DisplayName);
          });
});
```

Both paths write to `SchemataMappingOptions` through `IOptions<SchemataMappingOptions>`, so mappings registered from either location are merged when the engine is initialized.

## Mapster integration

The `Schemata.Mapping.Mapster` package provides the Mapster engine adapter.

### Activation

```csharp
// Shorthand -- calls UseMapping().UseMapster() internally
builder.UseSchemata(schema => {
    schema.UseMapster();
});

// Explicit two-step form
builder.UseSchemata(schema => {
    schema.UseMapping().UseMapster();
});
```

`UseMapster()` on `SchemataMappingBuilder` does three things:

1. Registers a singleton `TypeAdapterConfig` that clones `TypeAdapterConfig.GlobalSettings`, enables `IgnoreNullValues` and `PreserveReference` as defaults, and applies all compiled `SchemataMappingOptions` mappings through `MapsterConfigurator`.
2. Registers the `SchemataMappingFeature<SimpleMapper>` feature, which adds `ISimpleMapper` as a scoped service backed by `Schemata.Mapping.Mapster.SimpleMapper`.
3. Returns the builder for chaining.

`MapsterConfigurator` groups the `IMapping` collection by (SourceType, DestinationType), then for each group calls `config.NewConfig<TSource, TDestination>()` and translates each mapping:

- A converter expression becomes `setter.MapWith(converter)`.
- An ignored field becomes `setter.Ignore(member)`.
- A field mapping becomes `setter.Map(member, expression)`, optionally followed by `setter.IgnoreIf(predicate, member)` when a condition is present.

## AutoMapper integration

The `Schemata.Mapping.AutoMapper` package provides the AutoMapper engine adapter.

### Activation

```csharp
// Shorthand
builder.UseSchemata(schema => {
    schema.UseAutoMapper();
});

// Explicit two-step form
builder.UseSchemata(schema => {
    schema.UseMapping().UseAutoMapper();
});
```

`UseAutoMapper()` on `SchemataMappingBuilder` does three things:

1. Registers a singleton `MapperConfiguration` that applies all compiled `SchemataMappingOptions` mappings through `AutoMapperConfigurator`, passing the application's `ILoggerFactory` to AutoMapper.
2. Registers the `SchemataMappingFeature<SimpleMapper>` feature, which adds `ISimpleMapper` as a scoped service backed by `Schemata.Mapping.AutoMapper.SimpleMapper`.
3. Returns the builder for chaining.

`AutoMapperConfigurator` groups mappings by (SourceType, DestinationType) and for each group calls `config.CreateMap<TSource, TDestination>()`. It then applies two global defaults for the map -- `ForAllMembers` with a null-check condition and `PreserveReferences` -- before translating each mapping:

- A converter expression becomes `setter.ConvertUsing(converter)`.
- An ignored field becomes `setter.ForMember(member, opts => opts.Ignore())`.
- A field mapping becomes `setter.ForMember(member, opts => opts.MapFrom(expression))`, optionally with a condition that inverts the ignore predicate (AutoMapper's `Condition` means "map when true", while Schemata's predicate means "ignore when true", so the configurator compiles the predicate and negates it).

## Collection extensions

`MapperExtensions` provides convenience methods for mapping collections:

| Method           | Signature                                                                       | Description                        |
| ---------------- | ------------------------------------------------------------------------------- | ---------------------------------- |
| `Each<T>`        | `IEnumerable<T> Each<T>(IEnumerable<object>, CancellationToken)`                | Maps each element, skipping nulls. |
| `Each<S,D>`      | `IEnumerable<D> Each<S,D>(IEnumerable<S>, CancellationToken)`                   | Strongly-typed variant.            |
| `EachAsync<T>`   | `IAsyncEnumerable<T> EachAsync<T>(IAsyncEnumerable<object>, CancellationToken)` | Async variant, skipping nulls.     |
| `EachAsync<S,D>` | `IAsyncEnumerable<D> EachAsync<S,D>(IAsyncEnumerable<S>, CancellationToken)`    | Async strongly-typed variant.      |

All four methods yield lazily and skip null mapping results.

## Resource pipeline usage

`ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>` receives `ISimpleMapper` through constructor injection and uses it in every CRUD operation:

| Operation  | Mapping call                                                             | Purpose                                                                                                   |
| ---------- | ------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| **List**   | `_mapper.EachAsync<TEntity, TSummary>(entities, ct)`                     | Converts the async entity stream into summary DTOs.                                                       |
| **Get**    | `_mapper.Map<TEntity, TDetail>(entity)`                                  | Converts a single entity to a detail DTO.                                                                 |
| **Create** | `_mapper.Map<TRequest, TEntity>(request)`                                | Converts the request DTO to a new entity.                                                                 |
| **Create** | `_mapper.Map<TEntity, TDetail>(entity)`                                  | Converts the persisted entity back to a detail DTO for the response.                                      |
| **Update** | `_mapper.Map(request, entity, fields)` or `_mapper.Map(request, entity)` | Applies request fields onto the existing entity, using field-level mapping when `IUpdateMask` is present. |
| **Update** | `_mapper.Map<TEntity, TDetail>(entity)`                                  | Converts the updated entity to a detail DTO for the response.                                             |
