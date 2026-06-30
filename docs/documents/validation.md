# Validation

`Schemata.Validation.FluentValidation` integrates [FluentValidation](https://docs.fluentvalidation.net/)
into the resource validation pipeline. Registering a validator with
`IServiceCollection.AddValidator<TValidator>()` wires two open-generic advisors as
`Schemata.Validation.Skeleton.Advisors.IValidationAdvisor<T>`, and the resource request pipeline
runs them against any request whose type has a registered validator.

## Packages

| Package                                | Key types                                                                                        |
| -------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `Schemata.Validation.Skeleton`         | `Advisors.IValidationAdvisor<T>`                                                                 |
| `Schemata.Validation.FluentValidation` | `AddValidator<TValidator>`, `Advisors.AdviceValidation<T>`, `Advisors.AdviceValidationErrors<T>` |

## IValidationAdvisor

```csharp
namespace Schemata.Validation.Skeleton.Advisors;

public interface IValidationAdvisor<in T>
    : IAdvisor<Operations, T, IList<ErrorFieldViolation>>;
```

Each advisor receives the CRUD `Operations` kind, the request being validated, and a mutable
`IList<ErrorFieldViolation>` to populate. Advisors run in `Order` sequence. A collecting advisor
appends violations and returns `AdviseResult.Continue`; a terminal advisor inspects the list and
returns `AdviseResult.Block` when it is non-empty.

## Registration

`AddValidator` is an extension on `IServiceCollection`, reachable through `schema.Services`:

```csharp
builder.UseSchemata(schema => {
    schema.UseRouting();
    schema.UseControllers();
    schema.UseResource().MapHttp().Use<Student, StudentRequest>();

    schema.Services.AddValidator<StudentRequestValidator>();
});
```

`AddValidator<TValidator>()` reflects the validator's `IValidator<T>` interface to find the request
type. The two-argument overload `AddValidator<T, TValidator>()` states the request type explicitly:

```csharp
schema.Services.AddValidator<StudentRequest, StudentRequestValidator>();
```

Both overloads take an optional `ServiceLifetime` (default `Scoped`). The shared helper performs
three registrations:

1. `TryAdd` the validator as `IValidator<T>` with the chosen lifetime.
2. `TryAddEnumerable` `AdviceValidation<>` as `IValidationAdvisor<>`.
3. `TryAddEnumerable` `AdviceValidationErrors<>` as `IValidationAdvisor<>`.

`TryAddEnumerable` registers each advisor open-generic once, regardless of how many validators are
added. `TryAdd` registers one `IValidator<T>` per request type; a second `AddValidator` call for the
same type is ignored.

## Advisor behavior

`Schemata.Validation.FluentValidation.Advisors.AdviceValidation<T>`
(`Order = Orders.Base`, 100,000,000) resolves `IValidator<T>` from DI, builds a
`ValidationContext<T>` carrying the `Operations` kind in `RootContextData`, and runs `ValidateAsync`.
Each FluentValidation failure becomes an `ErrorFieldViolation`:

- `Field` — `PropertyName` in `snake_case`.
- `Reason` — the error code with the `Validator` suffix stripped and converted to AIP-193
  UPPER_SNAKE_CASE (e.g. `INCLUSIVE_BETWEEN`, `MAXIMUM_LENGTH`, `NOT_EMPTY`). Reasons stay literal
  keys; comparison operands stay in `Description`.
- `Description` — the formatted FluentValidation message, including any operand values that the
  template renders (e.g. `"Age must be between 1 and 150."`).

It returns `AdviseResult.Continue` whether or not it found errors; collection, not short-circuit, is
its job.

`AdviceValidationErrors<T>` (`Order = Orders.Base + 10_000_000`, 110,000,000) is the terminal
advisor: it returns `AdviseResult.Block` when the violation list is non-empty, otherwise
`AdviseResult.Continue`.

## Resource pipeline integration

The resource request pipeline runs the `IValidationAdvisor<TRequest>` chain through
`AdviceCreateRequestValidation` / `AdviceUpdateRequestValidation`, after the authorize and sanitize
advisors. `Schemata.Resource.Foundation.Advisors.ValidationHelper.ValidateAsync` drives the chain:

1. Runs the `IValidationAdvisor<TRequest>` advisors over a fresh `List<ErrorFieldViolation>`.
2. On `AdviseResult.Block`, throws `Schemata.Abstractions.Exceptions.ValidationException(errors)`,
   which maps to `INVALID_ARGUMENT` / HTTP 422 with a `BadRequestDetail`.
3. When the request implements `IValidation` with `ValidateOnly = true`, throws
   `NoContentException` after validation passes, so a dry run never persists.

A request type with no registered `IValidator<T>` makes `AdviceValidation<T>` return immediately;
the chain produces no violations and the operation proceeds.

## Validator implementation

Validators are plain FluentValidation `AbstractValidator<T>` subclasses.

```csharp
public sealed class StudentRequestValidator : AbstractValidator<StudentRequest>
{
    public StudentRequestValidator() {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Age).InclusiveBetween(1, 150);
    }
}
```

## Extension points

| Interface               | Purpose                                                                                                                                                                                                                                          |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `IValidationAdvisor<T>` | Add validation logic outside FluentValidation. Register via `TryAddEnumerable` and choose an `Order` between the collector (100,000,000) and the terminal (110,000,000) to contribute violations, or after the terminal to react to the outcome. |
| `IValidator<T>`         | The FluentValidation entry point. Compose multiple rule sets for one type with `Include(new OtherValidator())` inside a single `AbstractValidator<T>`.                                                                                           |

## Caveats

- Registration is on `IServiceCollection`, not `SchemataBuilder`; there is no `UseValidation`
  builder method.
- `TryAdd` keeps the first `IValidator<T>` per request type. A second `AddValidator` for the same
  type has no effect.
- Validation runs in the resource request stage. A direct `IRepository<T>` call outside a resource
  handler runs the repository advisors, which include their own add/update validation advisors,
  rather than this chain.

## See also

- [Validation guide](../guides/validation.md)
- [Create Pipeline](resource/create-pipeline.md)
- [Advice Pipeline](core/advice-pipeline.md)
