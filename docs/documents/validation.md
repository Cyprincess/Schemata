# Validation

Schemata integrates FluentValidation into its advisor pipeline, providing automatic request and entity validation with dry-run support.

## Packages

| Package                                | Role                                                |
| -------------------------------------- | --------------------------------------------------- |
| `Schemata.Validation.Skeleton`         | Defines the `IValidationAdvisor<T>` interface       |
| `Schemata.Validation.FluentValidation` | FluentValidation bridge and advisor implementations |

## IValidationAdvisor\<T\>

The core validation contract lives in `Schemata.Validation.Skeleton.Advisors`:

```csharp
public interface IValidationAdvisor<in T> : IAdvisor<Operations, T, IList<ErrorFieldViolation>>;
```

Implementations receive:

- `Operations` -- the CRUD operation kind (`Create`, `Update`, etc.)
- `T` -- the object being validated
- `IList<ErrorFieldViolation>` -- a mutable list to populate with validation errors

Multiple advisors run in `Order` sequence. The final advisor blocks the pipeline when errors have accumulated.

## FluentValidation bridge

### AdviceValidation\<T\>

Resolves `IValidator<T>` from the service provider and runs validation. Each `ValidationFailure` is translated into an `ErrorFieldViolation`:

- `Field` -- the property name, converted to underscore_case via Humanizer
- `Reason` -- the error code with the `Validator` suffix stripped, converted to underscore_case, with comparison/range values appended (e.g. `greater_than_or_equal,5`)
- `Description` -- the original FluentValidation error message

The `Operations` value is placed into `ValidationContext<T>.RootContextData` under the key `"Operations"`, allowing validators to branch on the operation type.

Runs at order `SchemataConstants.Orders.Base`.

### AdviceValidationErrors\<T\>

A terminal advisor that runs 10,000,000 order positions after `AdviceValidation`. Returns `AdviseResult.Block` if any errors have accumulated, `AdviseResult.Continue` otherwise.

## Registration

Call `AddValidator<TValidator>()` or `AddValidator<T, TValidator>()` on `IServiceCollection`:

```csharp
services.AddValidator<CreateProductValidator>();
services.AddValidator<Product, ProductValidator>();
```

This registers:

1. The `IValidator<T>` implementation (scoped by default, configurable via the `lifetime` parameter)
2. `AdviceValidation<T>` as an `IValidationAdvisor<T>` (scoped, via `TryAddEnumerable`)
3. `AdviceValidationErrors<T>` as an `IValidationAdvisor<T>` (scoped, via `TryAddEnumerable`)

## Repository validation advisors

The repository layer integrates validation through two advisors that run before persistence:

### AdviceAddValidation\<TEntity\>

Implements `IRepositoryAddAdvisor<TEntity>`. Before an entity is added, it creates a fresh `List<ErrorFieldViolation>`, runs all `IValidationAdvisor<TEntity>` instances with `Operations.Create`, and throws `ValidationException` if the result is `AdviseResult.Block`.

Skipped when the advice context contains `SuppressAddValidation`.

### AdviceUpdateValidation\<TEntity\>

Implements `IRepositoryUpdateAdvisor<TEntity>`. Identical to the add variant but runs with `Operations.Update` and is skipped when `SuppressUpdateValidation` is present.

Both advisors are auto-registered by `AddRepository`.

## Resource-level validation

The resource layer provides its own validation advisors for request DTOs:

### AdviceCreateRequestValidation\<TEntity, TRequest\>

Runs after the authorization advisor in the create pipeline. Delegates to `IValidationAdvisor<TRequest>` with `Operations.Create`. Suppressed when `SuppressCreateRequestValidation` is present or when `SchemataResourceOptions.SuppressCreateValidation` is set.

### AdviceUpdateRequestValidation\<TEntity, TRequest\>

Same pattern for the update pipeline with `Operations.Update`.

Both use the shared `ValidationHelper` which also handles dry-run semantics.

## Dry-run with IValidation

Request DTOs can implement `IValidation` to support validation-only mode:

```csharp
public interface IValidation
{
    bool ValidateOnly { get; set; }
}
```

When `ValidateOnly` is `true`:

- If validation is suppressed, a `NoContentException` is thrown immediately (HTTP 204)
- If validation passes, a `NoContentException` is thrown after validation (HTTP 204) -- the entity is never persisted
- If validation fails, a `ValidationException` is thrown as usual (HTTP 422)

This allows clients to probe whether a request would succeed without side effects.

## Error model

### ErrorFieldViolation

```csharp
public class ErrorFieldViolation
{
    public string? Field { get; set; }
    public string? Description { get; set; }
    public string? Reason { get; set; }
}
```

### BadRequestDetail

Wraps a list of `ErrorFieldViolation` and implements `IErrorDetail` with type `"type.googleapis.com/google.rpc.BadRequest"`, following the Google API error model.

### ValidationException

Thrown when validation fails. HTTP status 422, error code `InvalidArgument`. Carries a `BadRequestDetail` containing the field violations:

```csharp
public sealed class ValidationException : SchemataException
{
    public ValidationException(
        IEnumerable<ErrorFieldViolation> errors,
        int status = 422,
        string? code = ErrorCodes.InvalidArgument,
        string? message = null
    );
}
```
