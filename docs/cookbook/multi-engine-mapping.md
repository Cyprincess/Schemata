# Multi-Engine Mapping

## What you'll build

A Schemata application that starts with Mapster as the mapping engine, then switches to AutoMapper with a single call change. You'll also configure an `UpdateMask` so partial updates only overwrite the fields the client explicitly sends.

## Prerequisites

- The Student example from [Getting Started](../guides/getting-started.md) is running.
- NuGet packages: `Schemata.Mapping.Foundation`, plus either `Schemata.Mapping.Mapster` or `Schemata.Mapping.AutoMapper`.

## Step 1: Enable the mapping subsystem with Mapster

Call `UseMapster()` on the `SchemataBuilder`. This is a shorthand that calls `UseMapping().UseMapster()` internally.

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();
        schema.UseMapster();
        schema.UseResource().MapHttp().Use<Student, StudentRequest, StudentDetail, StudentSummary>();
    });
```

`UseMapster()` registers a `TypeAdapterConfig` singleton with `IgnoreNullValues(true)` and `PreserveReference(true)` applied globally, then adds `SchemataMappingFeature<SimpleMapper>`.

**Assertion:** Start the app and `POST /students` with a partial body. Fields absent from the request body are not overwritten on the entity because Mapster skips null source values.

## Step 2: Switch to AutoMapper

Replace `UseMapster()` with `UseAutoMapper()`. No other code changes are required.

```csharp
schema.UseAutoMapper();
```

`UseAutoMapper()` registers a `MapperConfiguration` singleton built from `SchemataMappingOptions`, then adds the same `SchemataMappingFeature<SimpleMapper>`. The `ISimpleMapper` abstraction is identical from the handler's perspective.

**Assertion:** The app starts and maps correctly. AutoMapper logs its configuration at startup via the `ILoggerFactory` passed to `MapperConfiguration`.

## Step 3: Define explicit field maps

Both engines read from `SchemataMappingOptions`. Register field maps before calling `UseAutoMapper()` or `UseMapster()`:

```csharp
schema.Services.Configure<SchemataMappingOptions>(o => {
    o.Add<StudentRequest, Student>()
     .For(dest => dest.DisplayName)
     .From(src => src.FullName);
});
schema.UseAutoMapper();
```

`Map<TSource, TDestination>.Compile()` validates that every non-ignored, non-converter field mapping has a source expression. It throws `InvalidOperationException` at startup if a destination field is declared without a source.

**Assertion:** Rename a field in the map and restart. The app throws at startup rather than silently producing wrong data at runtime.

## Step 4: Apply an UpdateMask for partial updates

`SimpleMapperHelper.MapWithMask` is the mechanism behind partial updates. The resource handler calls it when the request carries an `UpdateMask`. You don't need to call it directly, but understanding it helps when writing custom advisors.

```csharp
// Illustrative: what the handler does internally
SimpleMapperHelper.MapWithMask(
    source:      request,
    destination: entity,
    mask:        request.UpdateMask.Paths,   // e.g. ["display_name", "description"]
    mapAction:   (src, dest) => mapper.Map(src, dest)
);
```

The helper saves the current values of all writable properties on `destination` that are **not** in the mask, runs the full map, then restores the saved values. Only the masked fields end up changed.

**Assertion:** Send a `PATCH /students/{name}` with `update_mask: { paths: ["display_name"] }`. Inspect the entity after the call. Only `DisplayName` changed; all other fields retain their pre-update values.

## Step 5: Verify the engine is active

Both engines register `ICacheProvider` under the same `ISimpleMapper` interface. To confirm which engine is active at runtime:

```csharp
app.MapGet("/debug/mapper", (ISimpleMapper mapper) => mapper.GetType().FullName);
```

**Assertion:** The response contains `AutoMapper.SimpleMapper` or `Mapster.SimpleMapper` depending on which `Use*` call is in startup.

## Common pitfalls

- **Calling both `UseAutoMapper()` and `UseMapster()`** registers two `SchemataMappingFeature<SimpleMapper>` instances. The second `TryAddSingleton` call for the engine config is silently ignored, so whichever engine was registered first wins. Call only one.
- **`Map.Compile()` throws at startup, not at map time.** A missing source expression on a non-ignored field is a startup error, not a runtime null. Check the exception message for the destination type name.
- **`IgnoreNullValues` is Mapster-only.** AutoMapper skips null source members only when you configure `AllowNullCollections` or use `Condition`. If you rely on null-skip behavior, test after switching engines.
- **`UpdateMask` paths are wire names (snake_case).** The mask paths arrive as `display_name`, not `DisplayName`. `SimpleMapperHelper` uses `StringComparer.OrdinalIgnoreCase` against the CLR property name, so the casing mismatch is handled, but the underscore-to-PascalCase conversion is not. Pass CLR property names in the mask or use the `SchemataNaming.ToClrMemberName` helper before calling `MapWithMask`.

## See also

- [Object Mapping guide](../guides/object-mapping.md)
- [Mapping overview](../documents/mapping/overview.md)
- [AutoMapper adapter](../documents/mapping/automapper.md)
- [Mapster adapter](../documents/mapping/mapster.md)
