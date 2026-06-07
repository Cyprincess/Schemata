# Validation

Add server-side input validation using FluentValidation. Registration goes through `IServiceCollection.AddValidator<TValidator>()` — there is no `UseValidation` method on `SchemataBuilder`. This guide builds on [Object Mapping](object-mapping.md).

## How it works

`AddValidator<TValidator>()` is an extension method on `IServiceCollection` from `Schemata.Validation.FluentValidation`. It:

1. Registers `TValidator` as `IValidator<T>` (scoped by default).
2. Auto-registers `AdviceValidation<T>` and `AdviceValidationErrors<T>` so the resource pipeline runs validation automatically.

The resource pipeline runs validation advisors at `Order = 200_000_000` — after authorization and sanitization, before the entity is mapped and persisted. A failed validation throws `ValidationException` (HTTP 422).

`StudentRequest` implements `IValidation` from [Object Mapping](object-mapping.md). The `ValidateOnly` property enables dry-run validation: when `true`, the pipeline validates and returns HTTP 204 without persisting.

## Add the package

`Schemata.Application.Complex.Targets` already includes `Schemata.Validation.FluentValidation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Validation.FluentValidation
```

## Create the validator

Create `StudentRequestValidator.cs`:

```csharp
using FluentValidation;

public class StudentRequestValidator : AbstractValidator<StudentRequest>
{
    public StudentRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Age)
            .InclusiveBetween(1, 150);
    }
}
```

The validator targets `StudentRequest` — the `TRequest` type in the resource pipeline. Validation runs against the request DTO, not the entity.

## Register the validator

Inside the `ConfigureServices` block in `Program.cs`:

```csharp
schema.ConfigureServices(services => {
    services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>(
            (_, opts) => opts.UseSqlite("Data Source=app.db"));

    services.TryAddEnumerable(
        ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());

    services.AddValidator<StudentRequestValidator>();
});
```

The two-parameter overload `AddValidator<T, TValidator>()` is available when the validator type does not directly reveal its target via generic interfaces.

## Verify

```shell
dotnet run
```

Trigger validation errors:

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"","age":200}'
```

Response (HTTP 422):

```json
{
  "error": {
    "code": 422,
    "status": "INVALID_ARGUMENT",
    "message": "One or more validation errors occurred.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.BadRequest",
        "field_violations": [
          { "field": "full_name", "reason": "not_empty",              "description": "'Full Name' must not be empty." },
          { "field": "age",       "reason": "inclusive_between,1,150", "description": "'Age' must be between 1 and 150." }
        ]
      }
    ]
  }
}
```

Dry-run validation (validates without persisting):

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20,"validate_only":true}'
```

A valid request returns HTTP 204. An invalid request returns HTTP 422 with the same error shape.

## See also

- [Query Caching](query-caching.md) — previous in the series: transparent query result caching
- [Identity](identity.md) — next in the series: user management with ASP.NET Core Identity
- [Object Mapping](object-mapping.md) — `StudentRequest` with `IValidation`
- [Validation](../documents/validation.md) — `AddValidator` internals, advisor registration
- [Error Model](../documents/core/error-model.md) — `ValidationException` and HTTP 422 shape
