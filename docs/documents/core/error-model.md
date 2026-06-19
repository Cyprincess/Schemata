# Error Model

Schemata returns structured errors per [Google AIP-193](https://google.aip.dev/193). Every error
carries an HTTP status code, a canonical `google.rpc.Code` string, a developer message, and typed
detail entries on the exception itself. The HTTP exception-handler middleware turns any
`SchemataException` into the JSON envelope automatically; any other exception becomes a generic
500 with a request trace identifier.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Abstractions` | `Exceptions/SchemataException.cs` and one file per subclass |
| `Schemata.Abstractions` | `Errors/ErrorResponse.cs`, `Errors/ErrorBody.cs`, `Errors/IErrorDetail.cs` |
| `Schemata.Abstractions` | `Errors/BadRequestDetail.cs`, `Errors/ErrorFieldViolation.cs`, `Errors/ErrorInfoDetail.cs`, `Errors/PreconditionFailureDetail.cs`, `Errors/QuotaFailureDetail.cs`, `Errors/RequestInfoDetail.cs`, `Errors/ResourceInfoDetail.cs` |
| `Schemata.Abstractions` | `SchemataConstants.cs` (`ErrorCodes`, `ErrorReasons`, `FieldReasons`) |
| `Schemata.Transport.Http` | `Features/SchemataTransportHttpFeature.cs` |

## Envelope

Errors serialize inside an `ErrorResponse` wrapper:

```json
{
  "error": {
    "code": 404,
    "status": "NOT_FOUND",
    "message": "The requested resource was not found.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.ErrorInfo",
        "reason": "NOT_FOUND"
      },
      {
        "@type": "type.googleapis.com/google.rpc.RequestInfo",
        "request_id": "0HMVQJ6K1TPKL:00000001"
      }
    ]
  }
}
```

### ErrorResponse and ErrorBody

`ErrorResponse` holds a single `Error` property of type `ErrorBody`. `ErrorBody` carries:

| Property | Type | Description |
| --- | --- | --- |
| `code` | `int` | The HTTP status code (mirrors `google.rpc.Status.code`, e.g. `404`) |
| `status` | `string?` | The canonical `google.rpc.Code` name (e.g. `"NOT_FOUND"`) for client-side branching |
| `message` | `string?` | Developer-oriented diagnostic message, not localized for display |
| `details` | `List<IErrorDetail>?` | Typed detail entries; each serializes with an `@type` discriminator |

### IErrorDetail

`IErrorDetail` is a bare marker interface. Each detail class is registered as a polymorphic derived
type with `[Polymorphic(typeof(IErrorDetail), Name = "type.googleapis.com/google.rpc.<Kind>")]`, and
`PolymorphicTypeResolver` emits that `Name` as the `@type` discriminator. The type URL lives on the
attribute, not on a property.

## Exception hierarchy

Every domain exception derives from `SchemataException`:

```
Exception
  SchemataException
    AlreadyExistsException        InvalidArgumentException     QuotaExceededException
    AuthorizationException        NoContentException           TenantResolveException
    ConcurrencyException          NotFoundException            UnauthenticatedException
    FailedPreconditionException   OAuthException               ValidationException
```

### SchemataException

The constructor is `SchemataException(int code, string? status = null, string? message = null)`:

| Property | Type | Holds |
| --- | --- | --- |
| `Code` | `int` | The HTTP response status code |
| `Status` | `string?` | The canonical `google.rpc.Code` string |
| `Details` | `List<IErrorDetail>?` | Typed detail entries |

`CreateErrorResponse(requestId, domain)` builds the envelope: it inserts an `ErrorInfoDetail`
(with `reason` set to `Status`, falling back to `INTERNAL`) at the front of the detail list when
none is present, appends a `RequestInfoDetail` when a request id is supplied, and returns an
`ErrorResponse`. Subclasses override it to produce protocol-specific envelopes — `OAuthException`
returns an `OAuthErrorResponse` per RFC 6749, and `NoContentException` returns `null` so no body is
written.

### Exception types

| Exception | HTTP code | Canonical status | Default message |
| --- | --- | --- | --- |
| `InvalidArgumentException` | 400 | `INVALID_ARGUMENT` | The request contains an invalid argument. |
| `ValidationException` | 422 | `INVALID_ARGUMENT` | One or more validation errors occurred. |
| `NotFoundException` | 404 | `NOT_FOUND` | The requested resource was not found. |
| `AlreadyExistsException` | 409 | `ALREADY_EXISTS` | The resource already exists. |
| `AuthorizationException` | 403 | `PERMISSION_DENIED` | You do not have permission to perform this action. |
| `UnauthenticatedException` | 401 | `UNAUTHENTICATED` | The request does not have valid authentication credentials. |
| `ConcurrencyException` | 409 | `ABORTED` | A concurrency conflict occurred while saving to the database. |
| `FailedPreconditionException` | 412 | `FAILED_PRECONDITION` | The request cannot be executed in the current system state. |
| `TenantResolveException` | 400 | `FAILED_PRECONDITION` | Unable to resolve tenant for the current request. |
| `QuotaExceededException` | 429 | `RESOURCE_EXHAUSTED` | Rate limit exceeded. |
| `NoContentException` | 204 | `OK` | _(none)_ |

`ValidationException` takes a set of `ErrorFieldViolation` values and wraps them in a
`BadRequestDetail`. `NoContentException` signals a successful body-less response, used by
validate-only requests.

## Detail types

| Detail class | `@type` URL | Carries |
| --- | --- | --- |
| `BadRequestDetail` | `.../google.rpc.BadRequest` | `field_violations`: `List<ErrorFieldViolation>` |
| `ErrorInfoDetail` | `.../google.rpc.ErrorInfo` | `reason`, `domain`, `metadata` |
| `PreconditionFailureDetail` | `.../google.rpc.PreconditionFailure` | `violations`: `List<PreconditionViolation>` (`type`, `subject`, `description`) |
| `QuotaFailureDetail` | `.../google.rpc.QuotaFailure` | `violations`: `List<QuotaViolation>` (`subject`, `description`) |
| `RequestInfoDetail` | `.../google.rpc.RequestInfo` | `request_id`, `serving_data` |
| `ResourceInfoDetail` | `.../google.rpc.ResourceInfo` | `resource_type`, `resource_name`, `owner`, `description` |

Each `ErrorFieldViolation` carries `field` (snake_case path), `description`, and `reason`. With
FluentValidation, the reason is derived from the validator's error code by stripping the
`Validator` suffix and converting to `snake_case`; comparison values append as comma-separated
parameters (`maximum_length,100`). `ErrorInfoDetail.Reason` uses values such as
`SchemataConstants.ErrorReasons.ConcurrencyMismatch` (`"CONCURRENCY_MISMATCH"`).

## HTTP transport

`SchemataTransportHttpFeature` registers a global exception-handler middleware in its
`ConfigureApplication`:

- A caught `SchemataException` sets the response status to its `Code`, calls
  `CreateErrorResponse()` with the current `TraceIdentifier`, and writes the snake_case JSON body.
- Any other exception sets status 500 and returns an `ErrorBody` with status `INTERNAL`, a generic
  message, and a `RequestInfoDetail` carrying the `TraceIdentifier`. The original exception message
  stays server-side.

## Error codes

`SchemataConstants.ErrorCodes` defines the canonical strings: `Ok`, `InvalidArgument`, `NotFound`,
`PermissionDenied`, `Aborted`, `AlreadyExists`, `FailedPrecondition`, `Unauthenticated`,
`ResourceExhausted`, `Internal` — each mapping to its `google.rpc.Code` name.

## Design rationale

Carrying the HTTP status, canonical code, and typed details on the exception lets the middleware
format every response without catching individual types. Any code that throws a `SchemataException`
subclass produces a correct response. The `CreateErrorResponse` override point lets protocol
exceptions emit their own envelope.

## Caveats

- `ValidationException` uses HTTP 422, not 400, to separate validation failures from malformed
  requests.
- `ConcurrencyException` uses HTTP 409 with status `ABORTED`, matching the `google.rpc.Code` table.
- `NoContentException` is a control-flow signal for validate-only requests; its
  `CreateErrorResponse` returns `null` and the response is HTTP 204 with no body.

## See also

- [JSON Serialization](json-serialization.md) — how `IErrorDetail` and `@type` are serialized
- [Built-in Features](built-in-features.md) — `SchemataTransportHttpFeature` (410M)
