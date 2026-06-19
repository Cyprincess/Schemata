# Validation

Add server-side input validation with FluentValidation. Registration goes through
`IServiceCollection.AddValidator<TValidator>()`; there is no `UseValidation` builder method. This
guide builds on [Object Mapping](object-mapping.md).

## How it works

`AddValidator<TValidator>()` is an extension on `IServiceCollection` from
`Schemata.Validation.FluentValidation`. It:

1. Registers `TValidator` as `IValidator<T>` (scoped by default).
2. Registers `AdviceValidation<T>` and `AdviceValidationErrors<T>` so the resource pipeline runs
   validation against any request whose type has a validator.

The resource request pipeline runs validation after authorization and sanitization, before the
request is mapped onto the entity. `AdviceValidation<T>` collects field violations;
`AdviceValidationErrors<T>` blocks when any were found, which surfaces as a `ValidationException`
(HTTP 422).

`StudentRequest` implements `IValidation` from [Object Mapping](object-mapping.md). Its
`ValidateOnly` property enables a dry run: a valid `validate_only` request returns HTTP 204 without
persisting.

## Add the package

`Schemata.Application.Complex.Targets` already pulls in `Schemata.Validation.FluentValidation`. To
compose packages by hand:

```shell
dotnet add package --prerelease Schemata.Validation.FluentValidation
```

## Create the validator

`StudentRequestValidator.cs`:

```csharp
using FluentValidation;

public sealed class StudentRequestValidator : AbstractValidator<StudentRequest>
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

The validator targets `StudentRequest`, the `TRequest` type in the resource pipeline. Validation
runs against the request DTO, before it is mapped onto the `Student` entity.

## Register the validator

Add the validator inside the schema configuration:

```csharp
schema.Services.AddValidator<StudentRequestValidator>();
```

When the validator type does not directly reveal its target through its generic interface, name the
request type explicitly:

```csharp
schema.Services.AddValidator<StudentRequest, StudentRequestValidator>();
```

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

The `reason` code is the FluentValidation error code in `snake_case`, with comparison operands
appended. Dry-run validation validates without persisting:

```shell
curl -X POST http://localhost:5000/students \
     -H "Content-Type: application/json" \
     -d '{"full_name":"Alice","age":20,"validate_only":true}'
```

A valid `validate_only` request returns HTTP 204; an invalid one returns the same HTTP 422 shape.

## Next steps

- [Identity](identity.md) — add users and login, then validate the identity request bodies
- [Concurrency and Freshness](concurrency-and-freshness.md) — `StudentRequest` already implements `IFreshness`
- [Filtering and Pagination](filtering-and-pagination.md) — query the list endpoint

## See also

- [Validation reference](../documents/validation.md) — `AddValidator` internals, advisor ordering
- [Error Model](../documents/core/error-model.md) — `ValidationException` and the HTTP 422 shape
