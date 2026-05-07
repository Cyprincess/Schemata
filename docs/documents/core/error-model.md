# Error Model

Schemata uses a structured error model inspired by the [Google API error model](https://cloud.google.com/apis/design/errors). Every error response follows the same envelope format regardless of transport, and each exception type maps deterministically to an HTTP status code and a gRPC status code.

## Error Response Envelope

All errors are returned inside an `ErrorResponse` wrapper containing a single `Error` property of type `ErrorBody`.

### ErrorResponse

| Property | Type        | Description                          |
| -------- | ----------- | ------------------------------------ |
| `error`  | `ErrorBody` | The error body with code and details |

### ErrorBody

| Property  | Type                  | Description                                                       |
| --------- | --------------------- | ----------------------------------------------------------------- |
| `code`    | `string?`             | Machine-readable error code (e.g. `"INVALID_ARGUMENT"`)           |
| `message` | `string?`             | Human-readable error message                                      |
| `details` | `List<IErrorDetail>?` | Structured detail objects, each carrying an `@type` discriminator |

### JSON Shape

Schemata serializes all error responses with `JsonNamingPolicy.SnakeCaseLower` and omits null properties (`JsonIgnoreCondition.WhenWritingNull`). The `IErrorDetail.Type` property is renamed to `@type` in the JSON output to follow Google API conventions.

A typical validation error response looks like this:

```json
{
  "error": {
    "code": "INVALID_ARGUMENT",
    "message": "One or more validation errors occurred.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.BadRequest",
        "field_violations": [
          {
            "field": "display_name",
            "reason": "not_empty",
            "description": "'Display Name' must not be empty."
          },
          {
            "field": "email",
            "reason": "email_address",
            "description": "'Email' is not a valid email address."
          }
        ]
      },
      {
        "@type": "type.googleapis.com/google.rpc.RequestInfo",
        "request_id": "0HMVQJ6K1TPKL:00000001"
      }
    ]
  }
}
```

A not-found error:

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "The requested resource was not found.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.RequestInfo",
        "request_id": "0HMVQJ6K1TPKL:00000002"
      }
    ]
  }
}
```

A concurrency conflict error:

```json
{
  "error": {
    "code": "ABORTED",
    "message": "A concurrency conflict occurred while saving to the database.",
    "details": [
      {
        "@type": "type.googleapis.com/google.rpc.ErrorInfo",
        "reason": "CONCURRENCY_MISMATCH"
      },
      {
        "@type": "type.googleapis.com/google.rpc.RequestInfo",
        "request_id": "0HMVQJ6K1TPKL:00000003"
      }
    ]
  }
}
```

## Error Detail Types

Every detail object implements `IErrorDetail`, which requires a `Type` property (serialized as `@type`) containing a fully-qualified type URL. Schemata ships the following detail types.

### BadRequestDetail

Type URL: `type.googleapis.com/google.rpc.BadRequest`

Describes field-level validation failures.

| Property           | Type                         | Description                                  |
| ------------------ | ---------------------------- | -------------------------------------------- |
| `field_violations` | `List<ErrorFieldViolation>?` | List of individual field validation failures |

### ErrorFieldViolation

Each entry in `field_violations` identifies one invalid field.

| Property      | Type     | Description                                        |
| ------------- | -------- | -------------------------------------------------- |
| `field`       | `string` | The field path that failed validation (snake_case) |
| `description` | `string` | Human-readable description of the violation        |
| `reason`      | `string` | Machine-readable reason code for the violation     |

The `reason` value is a free-form string, not an enum. When FluentValidation is used, the reason is derived from the FluentValidation error code by stripping the `Validator` suffix and converting to `snake_case`. Comparison values, ranges, and precision constraints are appended as comma-separated parameters.

Examples of generated `reason` values:

| FluentValidation Rule | Reason Value                          |
| --------------------- | ------------------------------------- |
| `NotEmpty`            | `not_empty`                           |
| `EmailAddress`        | `email_address`                       |
| `MaximumLength(100)`  | `maximum_length,100`                  |
| `Length(2, 50)`       | `length,2,50`                         |
| `GreaterThan(0)`      | `greater_than,0`                      |
| `InclusiveBetween`    | `inclusive_between,{from},{to}`       |
| `ScalePrecision`      | `scale_precision,{precision},{scale}` |

### ErrorInfoDetail

Type URL: `type.googleapis.com/google.rpc.ErrorInfo`

Provides structured information about the error, including a reason code and domain.

| Property   | Type                         | Description                                   |
| ---------- | ---------------------------- | --------------------------------------------- |
| `reason`   | `string`                     | Machine-readable reason code                  |
| `domain`   | `string`                     | Logical error domain (e.g. `"schemata.io"`)   |
| `metadata` | `Dictionary<string, string>` | Additional key-value metadata about the error |

The framework defines the following well-known reason value in `SchemataConstants.ErrorReasons`:

| Constant              | Value                  | Used By                |
| --------------------- | ---------------------- | ---------------------- |
| `ConcurrencyMismatch` | `CONCURRENCY_MISMATCH` | `ConcurrencyException` |

### PreconditionFailureDetail

Type URL: `type.googleapis.com/google.rpc.PreconditionFailure`

Describes one or more precondition violations that prevented the operation.

| Property     | Type                           | Description                     |
| ------------ | ------------------------------ | ------------------------------- |
| `violations` | `List<PreconditionViolation>?` | List of precondition violations |

Each `PreconditionViolation` has:

| Property      | Type     | Description                                        |
| ------------- | -------- | -------------------------------------------------- |
| `type`        | `string` | The precondition type (e.g. `"TENANT"`)            |
| `subject`     | `string` | The subject of the precondition (e.g. `"request"`) |
| `description` | `string` | Human-readable description                         |

Well-known precondition constants in `SchemataConstants`:

| Class                  | Constant  | Value     |
| ---------------------- | --------- | --------- |
| `PreconditionTypes`    | `Tenant`  | `TENANT`  |
| `PreconditionSubjects` | `Request` | `request` |

### QuotaFailureDetail

Type URL: `type.googleapis.com/google.rpc.QuotaFailure`

Describes one or more quota violations.

| Property     | Type                    | Description              |
| ------------ | ----------------------- | ------------------------ |
| `violations` | `List<QuotaViolation>?` | List of quota violations |

Each `QuotaViolation` has:

| Property      | Type     | Description                                       |
| ------------- | -------- | ------------------------------------------------- |
| `subject`     | `string` | The subject that exceeded the quota               |
| `description` | `string` | Human-readable description of the quota violation |

### RequestInfoDetail

Type URL: `type.googleapis.com/google.rpc.RequestInfo`

Contains request identification for debugging. The exception handler automatically appends this detail to every error response with `request_id` set to the ASP.NET Core `TraceIdentifier`.

| Property       | Type     | Description                         |
| -------------- | -------- | ----------------------------------- |
| `request_id`   | `string` | Unique request identifier           |
| `serving_data` | `string` | Opaque serving data for diagnostics |

### ResourceInfoDetail

Type URL: `type.googleapis.com/google.rpc.ResourceInfo`

Provides information about the resource involved in the error.

| Property        | Type     | Description                |
| --------------- | -------- | -------------------------- |
| `resource_type` | `string` | The type of resource       |
| `resource_name` | `string` | The name of the resource   |
| `owner`         | `string` | The owner of the resource  |
| `description`   | `string` | Human-readable description |

## Exception Hierarchy

All Schemata domain exceptions inherit from `SchemataException`, which carries the HTTP status code, machine-readable error code, and optional structured details.

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

The base exception for all Schemata domain errors.

| Property  | Type                  | Description                  |
| --------- | --------------------- | ---------------------------- |
| `Status`  | `int`                 | HTTP status code             |
| `Code`    | `string?`             | Machine-readable error code  |
| `Message` | `string`              | Human-readable error message |
| `Details` | `List<IErrorDetail>?` | Structured error details     |

The constructor accepts `status`, `code`, and `message` parameters. Subclasses provide defaults for all three.

### Exception Types

| Exception                     | HTTP Status | Error Code            | Default Message                                               | Attached Details                         |
| ----------------------------- | ----------- | --------------------- | ------------------------------------------------------------- | ---------------------------------------- |
| `InvalidArgumentException`    | 400         | `INVALID_ARGUMENT`    | The request contains an invalid argument.                     | --                                       |
| `ValidationException`         | 422         | `INVALID_ARGUMENT`    | One or more validation errors occurred.                       | `BadRequestDetail`                       |
| `NotFoundException`           | 404         | `NOT_FOUND`           | The requested resource was not found.                         | --                                       |
| `AlreadyExistsException`      | 409         | `ALREADY_EXISTS`      | The resource already exists.                                  | --                                       |
| `AuthorizationException`      | 403         | `PERMISSION_DENIED`   | You do not have permission to perform this action.            | --                                       |
| `UnauthenticatedException`    | 401         | `UNAUTHENTICATED`     | The request does not have valid authentication credentials.   | --                                       |
| `ConcurrencyException`        | 409         | `ABORTED`             | A concurrency conflict occurred while saving to the database. | `ErrorInfoDetail` (CONCURRENCY_MISMATCH) |
| `FailedPreconditionException` | 412         | `FAILED_PRECONDITION` | The request cannot be executed in the current system state.   | --                                       |
| `TenantResolveException`      | 400         | `FAILED_PRECONDITION` | Unable to resolve tenant for the current request.             | `PreconditionFailureDetail`              |
| `QuotaExceededException`      | 429         | `RESOURCE_EXHAUSTED`  | Rate limit exceeded.                                          | --                                       |
| `NoContentException`          | 204         | `OK`                  | _(none)_                                                      | --                                       |

`NoContentException` is a special case: it signals a successful operation with no response body (used by validate-only requests).

## Error Codes

All error code constants are defined in `SchemataConstants.ErrorCodes`:

| Constant             | Value                 |
| -------------------- | --------------------- |
| `Ok`                 | `OK`                  |
| `InvalidArgument`    | `INVALID_ARGUMENT`    |
| `NotFound`           | `NOT_FOUND`           |
| `PermissionDenied`   | `PERMISSION_DENIED`   |
| `Aborted`            | `ABORTED`             |
| `AlreadyExists`      | `ALREADY_EXISTS`      |
| `FailedPrecondition` | `FAILED_PRECONDITION` |
| `Unauthenticated`    | `UNAUTHENTICATED`     |
| `ResourceExhausted`  | `RESOURCE_EXHAUSTED`  |
| `Internal`           | `INTERNAL`            |

## Field Reason Codes

`SchemataConstants.FieldReasons` defines well-known reason codes used in `ErrorFieldViolation.Reason` for framework-produced validation errors:

| Constant                 | Value                      | Description                               |
| ------------------------ | -------------------------- | ----------------------------------------- |
| `NotEmpty`               | `not_empty`                | The field must not be empty               |
| `InvalidPayload`         | `invalid_payload`          | The request payload is invalid            |
| `InvalidFilter`          | `invalid_filter`           | The filter expression is malformed        |
| `InvalidName`            | `invalid_name`             | The resource name is invalid              |
| `InvalidOrderBy`         | `invalid_order_by`         | The order_by expression is malformed      |
| `InvalidPageToken`       | `invalid_page_token`       | The page token is invalid or expired      |
| `CrossParentUnsupported` | `cross_parent_unsupported` | Cross-parent operations are not supported |

These are distinct from the FluentValidation-derived reason codes described in the `ErrorFieldViolation` section above. FluentValidation reasons use `snake_case` (e.g. `not_empty`), while framework-produced reasons use `UPPER_SNAKE_CASE`.

## HTTP Transport

`SchemataExceptionHandlerFeature` registers a global exception handler middleware that converts exceptions to structured JSON responses.

When a `SchemataException` is caught:

1. The HTTP response status code is set to `SchemataException.Status`.
2. An `ErrorBody` is built from the exception's `Code`, `Message`, and `Details`.
3. A `RequestInfoDetail` with the current `TraceIdentifier` is appended to the details list.
4. The `ErrorResponse` envelope is serialized to JSON with snake_case naming and written to the response.

When any other exception is caught (non-`SchemataException`):

1. The HTTP response status code is set to 500.
2. An `ErrorBody` with code `INTERNAL` and message `"An error occurred."` is returned.
3. A `RequestInfoDetail` with the current `TraceIdentifier` is included.

The exception's original message is not leaked to the client for unhandled exceptions.

## gRPC Transport

`ExceptionMappingInterceptor` is a gRPC server interceptor that catches exceptions in unary calls and converts them to `RpcException` instances with structured status details.

When a `SchemataException` is caught, `RpcStatusBuilder` constructs a `Google.Rpc.Status` proto:

1. The error code string is mapped to a `Grpc.Core.StatusCode` using the mapping table below.
2. The exception's `Details` list is converted to protobuf `Any` messages and added to the status.
3. A `RequestInfo` detail with the current `TraceIdentifier` is appended.
4. The serialized status is attached to the `RpcException` via the `grpc-status-details-bin` metadata key.

When any other exception is caught:

1. A generic `SchemataException` with status 500 and code `INTERNAL` is constructed.
2. The same conversion pipeline applies.

### Error Code to gRPC Status Code Mapping

| Error Code            | gRPC StatusCode      |
| --------------------- | -------------------- |
| `OK`                  | `OK`                 |
| `INVALID_ARGUMENT`    | `InvalidArgument`    |
| `NOT_FOUND`           | `NotFound`           |
| `PERMISSION_DENIED`   | `PermissionDenied`   |
| `ABORTED`             | `Aborted`            |
| `ALREADY_EXISTS`      | `AlreadyExists`      |
| `FAILED_PRECONDITION` | `FailedPrecondition` |
| `UNAUTHENTICATED`     | `Unauthenticated`    |
| `RESOURCE_EXHAUSTED`  | `ResourceExhausted`  |
| _(any other value)_   | `Internal`           |

### Protobuf Detail Type Mapping

Each `IErrorDetail` subclass is packed into the corresponding Google RPC protobuf type:

| Schemata Type               | Protobuf Type                    |
| --------------------------- | -------------------------------- |
| `BadRequestDetail`          | `google.rpc.BadRequest`          |
| `ErrorInfoDetail`           | `google.rpc.ErrorInfo`           |
| `ResourceInfoDetail`        | `google.rpc.ResourceInfo`        |
| `PreconditionFailureDetail` | `google.rpc.PreconditionFailure` |
| `QuotaFailureDetail`        | `google.rpc.QuotaFailure`        |
| `RequestInfoDetail`         | `google.rpc.RequestInfo`         |

## Validation Pipeline Integration

The resource validation pipeline collects `ErrorFieldViolation` entries from validation advisors and, when any violations are present, throws a `ValidationException` that bundles them into a `BadRequestDetail`.

The flow works as follows:

1. `ValidationHelper.ValidateAsync` creates an empty `List<ErrorFieldViolation>` and passes it through the `IValidationAdvisor<TRequest>` pipeline.
2. Each advisor (e.g. `AdviceValidation<T>` for FluentValidation) appends violations to the list without short-circuiting.
3. `AdviceValidationErrors<T>` runs last in the pipeline. If the errors list is non-empty, it returns `Block`.
4. When the pipeline result is `Block`, `ValidationHelper` throws `new ValidationException(errors)`, which wraps the violations in a `BadRequestDetail`.
5. The exception handler (HTTP or gRPC) converts the exception to a structured response.

If the request implements `IValidation` with `ValidateOnly = true`, the pipeline throws `NoContentException` after validation succeeds (or `ValidationException` if it fails), returning HTTP 204 without performing the actual operation.

## JSON Serialization Configuration

The `SchemataJsonSerializerFeature` configures `System.Text.Json` serialization for all error responses:

| Setting                  | Value                             |
| ------------------------ | --------------------------------- |
| `PropertyNamingPolicy`   | `JsonNamingPolicy.SnakeCaseLower` |
| `DictionaryKeyPolicy`    | `JsonNamingPolicy.SnakeCaseLower` |
| `DefaultIgnoreCondition` | `WhenWritingNull`                 |
| `NumberHandling`         | `AllowReadingFromString`          |

Polymorphic serialization of `IErrorDetail` uses a `$type` discriminator managed by `PolymorphicTypeResolver`. Additionally, the `IErrorDetail.Type` property (which holds the Google type URL) is renamed to `@type` in JSON output to follow Google API conventions.

Enum values are serialized as `kebab-case-lower` strings via `JsonStringEnumConverter`.
