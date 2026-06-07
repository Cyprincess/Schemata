# Validation

Schemata's validation layer integrates [FluentValidation](https://docs.fluentvalidation.net/) into the resource advisor pipeline. You register a validator on `IServiceCollection` via `AddValidator<TValidator>()`. The helper wires two open-generic advisors (`AdviceValidation<T>` and `AdviceValidationErrors<T>`) as `IValidationAdvisor<T>`, so every resource operation whose request type matches a registered validator runs validation at the request stage.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Validation.Skeleton` | `Advisors/IValidationAdvisor.cs` |
| `Schemata.Validation.FluentValidation` | `Extensions/ServiceCollectionExtensions.cs` — `AddValidator<TValidator>()` and `AddValidator<T, TValidator>()` |
| `Schemata.Validation.FluentValidation` | `Advisors/AdviceValidation.cs`, `Advisors/AdviceValidationErrors.cs` |

## Mechanism walkthrough

### 1. Register a validator

Call `AddValidator` on the `IServiceCollection` exposed by `SchemataBuilder.Services`:

```csharp
builder.UseSchemata(schema => {
    schema.UseRouting();
    schema.UseControllers();
    schema.UseResource().MapHttp().Use<Student, StudentRequest>();

    schema.Services.AddValidator<StudentRequestValidator>();
});
```

`AddValidator<TValidator>()` inspects the validator's implemented `IValidator<T>` interface to determine the entity type, then calls the shared private helper. The two-argument overload `AddValidator<T, TValidator>()` skips the reflection step when you want to be explicit:

```csharp
schema.Services.AddValidator<StudentRequest, StudentRequestValidator>();
```

Both overloads accept an optional `ServiceLifetime` parameter (default `Scoped`).

### 2. What the helper registers

The private `AddValidator(services, serviceType, implementationType, lifetime)` helper does three things:

1. `services.TryAdd(new ServiceDescriptor(typeof(IValidator<T>), typeof(TValidator), lifetime))` — registers the FluentValidation validator.
2. `services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IValidationAdvisor<>), typeof(AdviceValidation<>)))` — registers the validation advisor open-generic.
3. `services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IValidationAdvisor<>), typeof(AdviceValidationErrors<>)))` — registers the error-collection advisor open-generic.

`TryAddEnumerable` means the advisors are registered once regardless of how many validators you add.

### 3. Pipeline integration

The resource operation handler runs `IValidationAdvisor<TRequest>` at `Order = 200_000_000` in the request stage, after authorization (`Order = 100_000_000`) and sanitization (`Order = 150_000_000`). See [Create Pipeline](resource/create-pipeline.md) for the full lane table.

`AdviceValidation<T>` resolves `IValidator<T>` from DI and calls `ValidateAsync`. On failure it returns `AdviseResult.Block`, which short-circuits the operation before the entity is touched.

`AdviceValidationErrors<T>` collects `ValidationFailure` entries and stores them in `AdviceContext` so the response layer can surface structured error details.

### 4. Validator implementation

Validators are standard FluentValidation classes:

```csharp
public sealed class StudentRequestValidator : AbstractValidator<StudentRequest>
{
    public StudentRequestValidator() {
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Email).EmailAddress().When(r => r.Email is not null);
    }
}
```

No Schemata-specific base class is required. The validator must implement `IValidator<T>` (which `AbstractValidator<T>` satisfies).

## Extension points

| Interface | Purpose |
| --- | --- |
| `IValidationAdvisor<T>` | Implement to add custom validation logic outside FluentValidation. Register via `services.TryAddEnumerable`. |
| `IValidator<T>` | Standard FluentValidation entry point. Multiple validators for the same `T` are not merged — only the first registered wins via `TryAdd`. |

To run multiple validators for the same type, compose them inside a single `AbstractValidator<T>` using `Include(new OtherValidator())`.

## Design motivation

Validation activates the moment a validator is registered. Registration goes on `IServiceCollection` directly, so validation works in any DI context, including outside `UseSchemata`. The advisor is open-generic, so the pipeline picks up new validators without per-entity wiring.

The two-advisor split (`AdviceValidation` for the gate, `AdviceValidationErrors` for error collection) keeps the short-circuit logic separate from the error-formatting logic. Either can be replaced independently.

## Caveats

- There is no `UseValidation` extension on `SchemataBuilder`. Registration goes through `IServiceCollection.AddValidator<TValidator>()` on `schema.Services`.
- `TryAdd` means only the first registered `IValidator<T>` for a given `T` is used. If you call `AddValidator` twice for the same request type, the second call is silently ignored.
- The `ServiceLifetime` parameter defaults to `Scoped`. Validators that hold singleton-scoped dependencies should be registered with `ServiceLifetime.Singleton` explicitly.
- Validation runs in the resource request stage only. Repository-level operations (direct `IRepository<T>` calls outside a resource handler) do not trigger `IValidationAdvisor<T>` unless you invoke the advisor pipeline manually.

## See also

- [Create Pipeline](resource/create-pipeline.md) — validation lane order in the resource pipeline
- [Update Pipeline](resource/update-pipeline.md) — same advisor chain applies to update requests
- [Advice Pipeline](core/advice-pipeline.md) — how `IAdvisor` pipelines execute and short-circuit
- [Built-in Features](core/built-in-features.md) — feature priority reference
