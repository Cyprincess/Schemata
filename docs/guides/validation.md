# Validation

This guide builds on [Filtering and Pagination](filtering-and-pagination.md). You will add server-side input validation using FluentValidation, with automatic integration into the resource pipeline.

## How it works

Schemata's resource pipeline runs validation advisors before executing create and update operations. The `Schemata.Validation.FluentValidation` package bridges FluentValidation into this pipeline:

1. You register a FluentValidation validator with `AddValidator<TValidator>()`.
2. This auto-registers `AdviceValidation<T>`, which resolves `IValidator<T>` and runs validation, translating FluentValidation failures into `ErrorFieldViolation` entries.
3. It also auto-registers `AdviceValidationErrors<T>`, which blocks the pipeline when any violations exist.
4. The resource pipeline throws a `ValidationException` (HTTP 422) with the accumulated field violations.

`StudentRequest` already implements `IValidation` from [Object Mapping](object-mapping.md). The `ValidateOnly` property on `IValidation` allows dry-run validation: when set to `true`, the pipeline validates the request and returns HTTP 204 without persisting any changes.

## Add the FluentValidation package

The `Schemata.Application.Complex.Targets` meta-package already includes `Schemata.Validation.FluentValidation`, so no additional package reference is needed.

If you are using a minimal setup without the complex targets, add the package directly:

```shell
dotnet add package --prerelease Schemata.Validation.FluentValidation
```

## Create the validator

Create `StudentValidator.cs`:

```csharp
using FluentValidation;

public class StudentValidator : AbstractValidator<StudentRequest>
{
    public StudentValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Age)
            .InclusiveBetween(1, 150);
    }
}
```

The validator targets `StudentRequest` because that is the type used as `TRequest` in the resource pipeline. Validation runs against the request DTO, not the entity.

## Register the validator

In `Program.cs`, add the validator registration inside the `ConfigureServices` block:

```csharp
schema.ConfigureServices(services => {
    services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
        .UseEntityFrameworkCore<AppDbContext>(
            (_, opts) => opts.UseSqlite("Data Source=app.db"));

    services.TryAddEnumerable(
        ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentIdAdvisor>());

    // Register the FluentValidation validator
    services.AddValidator<StudentValidator>();
});
```

`AddValidator<TValidator>()` is an extension method on `IServiceCollection` provided by `Schemata.Validation.FluentValidation`. It:

1. Registers `StudentValidator` as `IValidator<StudentRequest>` (scoped by default).
2. Auto-registers `AdviceValidation<StudentRequest>` and `AdviceValidationErrors<StudentRequest>` so the resource pipeline runs validation automatically.

The method also has a two-parameter overload `AddValidator<T, TValidator>()` if the validator type does not directly reveal its target via generic interfaces.

## Full Program.cs

After all changes across this guide series:

```csharp
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;

var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseLogging();
        schema.UseRouting();
        schema.UseControllers();

        schema.UseMapster()
              .Map<Student, StudentDetail>()
              .Map<Student, StudentSummary>()
              .Map<StudentRequest, Student>();

        schema.ConfigureServices(services => {
            services.AddRepository(typeof(EntityFrameworkCoreRepository<,>))
                .UseEntityFrameworkCore<AppDbContext>(
                    (_, opts) => opts.UseSqlite("Data Source=app.db"));

            services.TryAddEnumerable(
                ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentIdAdvisor>());

            services.AddValidator<StudentValidator>();
        });

        schema.UseResource()
              .MapHttp()
              .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider
               .GetRequiredService<AppDbContext>()
               .Database.EnsureCreatedAsync();

app.Run();
```

## Error response format

When validation fails, the pipeline throws a `ValidationException` which produces an HTTP 422 response with the following structure:

```json
{
  "error": {
    "code": "INVALID_ARGUMENT",
    "message": "The request contains invalid arguments.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.BadRequest",
        "field_violations": [
          {
            "field": "full_name",
            "reason": "not_empty",
            "description": "'Full Name' must not be empty."
          },
          {
            "field": "age",
            "reason": "inclusive_between,1,150",
            "description": "'Age' must be between 1 and 150."
          }
        ]
      }
    ]
  }
}
```

Each `field_violations` entry contains:

| Field         | Description                                                                   |
| ------------- | ----------------------------------------------------------------------------- |
| `field`       | The property name in `snake_case`                                             |
| `reason`      | The FluentValidation error code, underscored, with comparison values appended |
| `description` | The human-readable error message                                              |

The `reason` field is derived from the FluentValidation error code (e.g., `NotEmptyValidator` becomes `not_empty`, `InclusiveBetweenValidator` becomes `inclusive_between,1,150` with the boundary values appended).

## Verify

```shell
dotnet run
```

### Trigger validation errors

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"","age":200}'
```

Response (HTTP 422):

```json
{
  "error": {
    "code": "INVALID_ARGUMENT",
    "message": "The request contains invalid arguments.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.BadRequest",
        "field_violations": [
          {
            "field": "full_name",
            "reason": "not_empty",
            "description": "'Full Name' must not be empty."
          },
          {
            "field": "age",
            "reason": "inclusive_between,1,150",
            "description": "'Age' must be between 1 and 150."
          }
        ]
      }
    ]
  }
}
```

### Dry-run validation

Set `validate_only` to `true` to validate without persisting:

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20,"validate_only":true}'
```

If the request is valid, the server responds with HTTP 204 (No Content) and no body. If validation fails, you get the same HTTP 422 error response shown above.

### Valid request

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20}'
```

Response (HTTP 201):

```json
{
  "id": 1742956800000,
  "full_name": "Alice",
  "age": 20,
  "name": "1742956800000",
  "etag": "W/\"dGVzdC10aW1lc3RhbXA\"",
  "create_time": "2026-03-26T12:00:00Z",
  "update_time": null
}
```

## Further reading

- [Validation](../documents/validation.md)
- [Error Model](../documents/core/error-model.md)
