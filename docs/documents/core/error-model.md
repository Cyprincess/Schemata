# Error Model

Schemata uses a structured error model per [Google AIP-193](https://google.aip.dev/193). Every error response follows the same envelope format regardless of transport, and each exception type maps deterministically to an HTTP status code and a canonical `google.rpc.Code` string. The exception handler middleware converts `SchemataException` instances to structured JSON automatically; unhandled exceptions produce a generic 500 response with a request trace identifier.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Abstractions` | `Exceptions/SchemataException.cs` |
| `Schemata.Abstractions` | `Errors/ErrorResponse.cs`, `Errors/ErrorBody.cs`, `Errors/IErrorDetail.cs` |
| `Schemata.Abstractions` | `Errors/BadRequestDetail.cs`, `Errors/ErrorFieldViolation.cs`, `Errors/ErrorInfoDetail.cs` |
| `Schemata.Abstractions` | `Errors/PreconditionFailureDetail.cs`, `Errors/QuotaFailureDetail.cs`, `Errors/RequestInfoDetail.cs`, `Errors/ResourceInfoDetail.cs` |
| `Schemata.Abstractions` | `SchemataConstants.cs` (ErrorCodes, ErrorReasons, FieldReasons) |
| `Schemata.Transport.Http` | `Features/SchemataTransportHttpFeature.cs` |

## Error response envelope

All errors are returned inside an `ErrorResponse` wrapper:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "The requested resource was not found.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.RequestInfo",
        "request_id": "0HMVQJ6K1TPKL:00000001"
      }
    ]
  }
}
```

### ErrorResponse

`ErrorResponse` is the top-level envelope. Its single property `Error` holds an `ErrorBody`.

### ErrorBody

| Property | Type | Description |
| --- | --- | --- |
| `code` | `string?` | Canonical error code from `google.rpc.Code` (e.g. `"NOT_FOUND"`) |
| `message` | `string?` | Developer-oriented diagnostic message; not localized for end-user display |
| `details` | `List<IErrorDetail>?` | Typed detail entries; each carries an `@type` discriminator |

### IErrorDetail

`IErrorDetail` is the marker interface for typed error details. It requires a `Type` property (serialized as `@type` in JSON output) containing a fully-qualified type URL.

## Exception hierarchy

All Schemata domain exceptions inherit from `SchemataException`:

```
Exception
  SchemataException
    AlreadyExistsException
    AuthorizationException
    ConcurrencyException
    FailedPreconditionException
    InvalidArgumentException
    NoContentException
    NotFoundException
    QuotaExceededException
    TenantResolveException
    UnauthenticatedException
    ValidationException
```

### SchemataException

The base exception for all Schemata domain errors. Constructor accepts `status`, `code`, and `message`. Subclasses provide defaults for all three.

| Property | Type | Description |
| --- | --- | --- |
| `Status` | `int` | HTTP response status code |
| `Code` | `string?` | Canonical error code for client-side branching |
| `Details` | `List<IErrorDetail>?` | Typed detail entries |

`CreateErrorResponse()` builds the `ErrorResponse` envelope. Subclasses may override it to produce protocol-specific envelopes — `OAuthException` returns an `OAuthErrorResponse` per RFC 6749 instead of the default `ErrorResponse`.

### Exception types

| Exception | HTTP Status | Error Code | Default message |
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

`NoContentException` is a special case: it signals a successful operation with no response body (used by validate-only requests).

## Error detail types

### BadRequestDetail

Type URL: `type.googleapis.com/google.rpc.BadRequest`

Describes field-level validation failures. Contains a `List<ErrorFieldViolation>` in `field_violations`.

Each `ErrorFieldViolation` has:

| Property | Type | Description |
| --- | --- | --- |
| `field` | `string` | The field path that failed validation (snake_case) |
| `description` | `string` | Human-readable description of the violation |
| `reason` | `string` | Machine-readable reason code |

When FluentValidation is used, the reason is derived from the FluentValidation error code by stripping the `Validator` suffix and converting to `snake_case`. Comparison values are appended as comma-separated parameters (e.g. `maximum_length,100`).

### ErrorInfoDetail

Type URL: `type.googleapis.com/google.rpc.ErrorInfo`

Provides structured information about the error. Properties: `reason` (string), `domain` (string), `metadata` (`Dictionary<string, string>`).

Well-known reason value: `SchemataConstants.ErrorReasons.ConcurrencyMismatch` = `"CONCURRENCY_MISMATCH"`, used by `ConcurrencyException`.

### PreconditionFailureDetail

Type URL: `type.googleapis.com/google.rpc.PreconditionFailure`

Contains a `List<PreconditionViolation>` in `violations`. Each violation has `type`, `subject`, and `description` strings.

Well-known constants: `SchemataConstants.PreconditionTypes.Tenant` = `"TENANT"`, `SchemataConstants.PreconditionSubjects.Request` = `"request"`.

### QuotaFailureDetail

Type URL: `type.googleapis.com/google.rpc.QuotaFailure`

Contains a `List<QuotaViolation>` in `violations`. Each violation has `subject` and `description` strings.

### RequestInfoDetail

Type URL: `type.googleapis.com/google.rpc.RequestInfo`

Contains request identification for debugging. Properties: `request_id` (string), `serving_data` (string).

`SchemataTransportHttpFeature` automatically appends this detail to every error response with `request_id` set to the ASP.NET Core `TraceIdentifier`.

### ResourceInfoDetail

Type URL: `type.googleapis.com/google.rpc.ResourceInfo`

Provides information about the resource involved in the error. Properties: `resource_type`, `resource_name`, `owner`, `description` (all strings).

## HTTP transport

`SchemataTransportHttpFeature` (in `Schemata.Transport.Http`) registers a global exception handler middleware in its `ConfigureApplication`. The middleware converts exceptions to structured JSON responses.

When a `SchemataException` is caught:

1. The HTTP response status code is set to `SchemataException.Status`.
2. `http.CreateErrorResponse()` builds the `ErrorResponse` envelope from the exception's `Code`, `Message`, and `Details`.
3. The response is serialized to JSON with snake_case naming and written to the response body.

When any other exception is caught:

1. The HTTP response status code is set to 500.
2. An `ErrorBody` with code `INTERNAL` and a generic message is returned.
3. A `RequestInfoDetail` with the current `TraceIdentifier` is included.

The original exception message is never leaked to the client for unhandled exceptions.

## Error codes

All error code constants are defined in `SchemataConstants.ErrorCodes`:

| Constant | Value |
| --- | --- |
| `Ok` | `OK` |
| `InvalidArgument` | `INVALID_ARGUMENT` |
| `NotFound` | `NOT_FOUND` |
| `PermissionDenied` | `PERMISSION_DENIED` |
| `Aborted` | `ABORTED` |
| `AlreadyExists` | `ALREADY_EXISTS` |
| `FailedPrecondition` | `FAILED_PRECONDITION` |
| `Unauthenticated` | `UNAUTHENTICATED` |
| `ResourceExhausted` | `RESOURCE_EXHAUSTED` |
| `Internal` | `INTERNAL` |

## Design motivation

Carrying the HTTP status code, canonical error code, and typed details on the exception itself means the exception handler middleware never needs to catch individual exception types. Any code that throws a `SchemataException` subclass gets a correctly-formatted response automatically. The `CreateErrorResponse` override point lets protocol-specific exceptions (OAuth) produce their own envelope without changing the middleware.

## Caveats

- `ValidationException` uses HTTP 422 (Unprocessable Entity), not 400, to distinguish validation failures from malformed requests.
- `ConcurrencyException` uses HTTP 409 with code `ABORTED` (not `CONFLICT`) to align with the `google.rpc.Code` mapping.
- `NoContentException` is not an error condition; it is a control-flow mechanism for validate-only requests that return HTTP 204.

## See also

- [JSON Serialization](json-serialization.md) — how `IErrorDetail` and `@type` are serialized
- [Built-in Features](built-in-features.md) — `SchemataTransportHttpFeature` priority (410M)
- [Feature System](feature-system.md) — `DependsOn` between `Transport.Http` and its dependencies
