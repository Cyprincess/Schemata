# Multi-Engine Mapping

Run one application against either mapping engine and switch between them with a single call. The
`ISimpleMapper` surface, the mapping declarations, and the field-mask behavior are identical across
AutoMapper and Mapster, so handler code never changes.

This recipe assumes the DTOs and `Map<,>` declarations from
[Object Mapping](../guides/object-mapping.md).

## Start with Mapster

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseMapster()
              .Map<Student, StudentDetail>()
              .Map<Student, StudentSummary>()
              .Map<StudentRequest, Student>();
        schema.UseResource()
              .MapHttp()
              .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
    });
```

`UseMapster()` clones `TypeAdapterConfig.GlobalSettings`, applies
`Default.IgnoreNullValues(true).PreserveReference(true)`, runs `MapsterConfigurator.Configure`, and
registers the result as a singleton. It then adds `SchemataMappingFeature<SimpleMapper>`, which
registers `ISimpleMapper` as scoped.

## Switch to AutoMapper

Replace the engine call. The `Map<,>` declarations move onto `UseAutoMapper()` unchanged:

```csharp
schema.UseAutoMapper()
      .Map<Student, StudentDetail>()
      .Map<Student, StudentSummary>()
      .Map<StudentRequest, Student>();
```

`UseAutoMapper()` builds a `MapperConfiguration` singleton from the same `SchemataMappingOptions`,
applying a global null-skip `Condition` and `PreserveReferences()`, then adds the same feature. Both
engines satisfy `ISimpleMapper`, so the resource handler is unaffected.

> AutoMapper validates its configuration when the singleton is built at startup. A field mapping
> that lacks a source expression throws `InvalidOperationException` from `Map<,>.Compile()` before
> the first request, naming the destination type.

## Custom field mappings carry over

Declarations use the engine-agnostic `For`/`From`/`Ignore`/`With` surface, so the same block works
on either engine:

```csharp
.Map<StudentRequest, Student>(map => {
    map.For(d => d.FullName).From(s => s.FullName);
    map.For(d => d.Nickname).Ignore((s, _) => s.FullName == "Hidden");
})
```

`AutoMapperConfigurator` and `MapsterConfigurator` translate these to `ForMember`/`ConvertUsing`
and `Map`/`Ignore`/`MapWith` respectively. A field with no source expression, ignore, or converter
fails `Compile()` at startup on both engines.

## Field masks behave identically

A `PATCH` carrying `update_mask` flows through `ISimpleMapper.Map(request, entity, fields)`, which
both adapters route through `SimpleMapperHelper.MapWithMask`. The resource handler converts the
wire-format mask (`update_mask: "full_name"`) to CLR property paths with `MaskTree.FromWire` before
the call, so the mask you send is `snake_case` and the mapper receives CLR names. An unknown mask
path is rejected as an invalid `update_mask`.

```shell
curl -X PATCH http://localhost:5000/v1/students/<name> \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Updated","update_mask":"full_name"}'
```

Only `full_name` changes. Masked fields are authoritative — a null source value for a masked field
clears the destination — while unmasked fields keep their pre-update values. Nested paths such as
`profile.display_name` traverse one level of objects; collection-element traversal is rejected.

## Confirm the active engine

```csharp
app.MapGet("/debug/mapper", (ISimpleMapper mapper) => mapper.GetType().FullName);
```

The response is `Schemata.Mapping.AutoMapper.SimpleMapper` or `Schemata.Mapping.Mapster.SimpleMapper`,
matching the `Use*` call in startup.

## Pitfalls

- `SchemataMappingFeature<T>` registers `ISimpleMapper` with `TryAddScoped`, so the first engine
  added wins. Call exactly one of `UseAutoMapper()` and `UseMapster()`.
- A masked field overwrites even when the request omits it: an absent JSON field deserializes to
  null, and a masked null leaf clears the destination. Mask only the fields the client means to set.

## See also

- [Mapping overview](../documents/mapping/overview.md)
- [AutoMapper adapter](../documents/mapping/automapper.md)
- [Mapster adapter](../documents/mapping/mapster.md)
