# Error Model

Schemata formats most `SchemataException` subclasses as structured errors per
[Google AIP-193](https://google.aip.dev/193). Each AIP-style exception carries an HTTP status code,
a canonical `google.rpc.Code` string, a developer message, and typed detail entries on the exception
itself. `OAuthException` instead emits the RFC 6749 OAuth error envelope. The HTTP
exception-handler middleware turns any `SchemataException` into its response envelope; any other
exception becomes a generic 500 with a request trace identifier.

## Where the code lives

| Package                   | Key files                                                                                                                                                                                                                        |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Abstractions`   | `Exceptions/SchemataException.cs` and one file per subclass                                                                                                                                                                      |
| `Schemata.Abstractions`   | `Errors/ErrorResponse.cs`, `Errors/ErrorBody.cs`, `Errors/IErrorDetail.cs`                                                                                                                                                       |
| `Schemata.Abstractions`   | `SchemataConstants.cs` (`ErrorCodes` for `google.rpc.Code` names; `ErrorReasons` for framework-default `ErrorInfo.reason` identifiers; `ErrorDomains` for the per-exception-family `ErrorInfo.domain` namespaces)                                                                                  |
| `Schemata.Transport.Http` | `Features/SchemataTransportHttpFeature.cs`                                                                                                                                                                                       |

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
        "reason": "RESOURCE_NOT_FOUND"
      },
      {
        "@type": "type.googleapis.com/google.rpc.RequestInfo",
        "request_id": "0HMVQJ6K1TPKL:00000001"
      }
    ]
  }
}
```

The `status` field carries the broad `google.rpc.Code` name; `reason` carries the
framework-default domain identifier from `SchemataConstants.ErrorReasons`
(`RESOURCE_NOT_FOUND` here). Throw sites with finer context override the default to a more
specific value such as `USER_NOT_FOUND`.

### ErrorResponse and ErrorBody

`ErrorResponse` holds a single `Error` property of type `ErrorBody`. `ErrorBody` carries:

| Property  | Type                  | Description                                                                         |
| --------- | --------------------- | ----------------------------------------------------------------------------------- |
| `code`    | `int`                 | The HTTP status code (e.g. `404`), rather than the numeric `google.rpc.Status.code` |
| `status`  | `string?`             | The canonical `google.rpc.Code` name (e.g. `"NOT_FOUND"`) for client-side branching |
| `message` | `string?`             | Developer-oriented diagnostic message, not localized for display                    |
| `details` | `List<IErrorDetail>?` | Typed detail entries; each serializes with an `@type` discriminator                 |

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
    PermissionDeniedException        NoContentException           TenantResolveException
    AbortedException          NotFoundException            UnauthenticatedException
    FailedPreconditionException   OAuthException               ValidationException
```

### SchemataException

The constructor is `SchemataException(int code, string? status = null, string? message = null)`:

| Property  | Type                  | Holds                                  |
| --------- | --------------------- | -------------------------------------- |
| `Code`    | `int`                 | The HTTP response status code          |
| `Status`  | `string?`             | The canonical `google.rpc.Code` string |
| `Details` | `List<IErrorDetail>?` | Typed detail entries                   |

### Status vs Reason

[AIP-193](https://google.aip.dev/193) and
[`google/rpc/error_details.proto`](https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto)
distinguish two independent identifiers, and the framework keeps them strictly separate:

- `Status` is the top-level `google.rpc.Code` name (`NOT_FOUND`, `ABORTED`, ...). It travels at
  the envelope root and provides broad classification.
- `ErrorInfoDetail.Reason` is a **domain-specific** UPPER_SNAKE_CASE identifier that
  **further-identifies** the error. Per the AIP, its existence is justified precisely because
  the ~20 top-level Codes cannot disambiguate errors across a real service surface.

Named framework exceptions use their `resourceKey` as `ErrorInfo.Reason`; that key identifies the
business template and its arguments. Domain exceptions may keep a separate canonical reason and
override `MessageResourceKey` for locale lookup. Caller-owned descriptions use an explicit factory
such as `OAuthException.FromDescription` or the base `SchemataException` constructor, rather than a
keyed constructor with a sentence as its key.

`CreateErrorResponse(requestId, locale)` builds the envelope. The `ErrorInfo.domain` value comes
from the exception itself through the overridable `SchemataException.Domain` property
(`SchemataConstants.ErrorDomains`); callers no longer pass a domain:

1. `EnsureErrorInfo` inserts a fallback `ErrorInfoDetail` whose `Reason` mirrors the Status only
   when the throw site supplied no `ErrorInfoDetail`, and assigns the exception's domain to every
   existing `ErrorInfoDetail` that lacks one. Explicit domains set by throw sites stay untouched.
   The insert path is a safety net for raw `SchemataException` throws, not a recommended shape —
   every named exception attaches its own domain-specific reason and bypasses the fallback.
2. `EnsureRequestInfo` appends a `RequestInfoDetail` when a request id is supplied.
3. `EnsureLocalizedMessage` resolves the exception's `MessageResourceKey` when supplied, otherwise
   `ErrorInfoDetail.Reason`, then the canonical status key. It substitutes named metadata into the
   locale-specific template. An absent locale or unresolvable resource leaves the detail absent.

Framework error producers construct exceptions from a resx template key plus named arguments
(`new OAuthException(error, SchemataResources.NOT_EMPTY, args)`, `new NotFoundException(resourceKey, args)`):
the developer-facing message renders once with the invariant culture and the localized detail
re-renders the same template with the same arguments for the requested locale, so the two
variants cannot drift apart.

`InsightValidationException` in `Schemata.Insight.Foundation/Planning` derives from the common base.
Its constructor accepts `(reason, resourceKey, metadata, innerException)`, attaches one `ErrorInfo`
with domain `schemata.insight`, and owns the HTTP/canonical classification: unknown source is
404/`NOT_FOUND`, unsupported plan is 501/`UNIMPLEMENTED`, and other request rejections are
400/`INVALID_ARGUMENT`. Both transport boundaries preserve that exception directly.

New framework templates are synchronized to every existing `Schemata.Abstractions/xlf` catalog.
English entries marked `state="new"` remain pending translation; their presence is not evidence of
a completed translation. Satellite production retains the framework's existing localization build
configuration.


Subclasses override `CreateErrorResponse` to produce protocol-specific envelopes — `OAuthException`
returns an `OAuthErrorResponse` per RFC 6749, propagates the uppercased OAuth `error` value into a
fallback `ErrorInfoDetail.Reason` (e.g. `invalid_grant` → `INVALID_GRANT`) when the exception
carries no `ErrorInfoDetail` (the resx-keyed constructor always attaches one), and percent-encodes any character outside the RFC 6749
`error_description` vocabulary (`%x20-21 / %x23-5B / %x5D-7E`) so the decoded description always
satisfies the wire contract. Extension points that own their validation text
(`IAuthorizationDetailTypeDescriptor.Validate`) build the exception through
`OAuthException.FromDescription(error, description)`. `NoContentException` returns `null` so no
body is written.

### SchemataResourceErrors factory

Resource-themed exceptions (`NOT_FOUND`, `ALREADY_EXISTS`, `FAILED_PRECONDITION`,
`PERMISSION_DENIED`, `ABORTED`) flow through
`SchemataResourceErrors`
in `Schemata.Common.Errors`. Each factory method pre-attaches:

- `ErrorInfoDetail` with the matching AIP-193 `Reason`,
- `ResourceInfoDetail` with `ResourceType` (derived from `ResourceNameDescriptor.ForType<T>().Singular`)
  and `ResourceName` (the supplied canonical name),
- For `PreconditionFailed<T>`, an additional `PreconditionFailureDetail` carrying a single
  `PreconditionViolation` whose `Subject` is the precondition identifier (e.g.
  `SchemataConstants.PreconditionSubjects.SoftDeleted`).

`Owner` is populated only on `PermissionDenied<T>`; `NotFound<T>` leaves it unset. This is a
Schemata policy. [AIP-211](https://google.aip.dev/211) requires failed authorization checks to
return `PERMISSION_DENIED` and recommends a message that allows for unknown resource existence; it
does not define `ResourceInfo.owner`.

```csharp
using Schemata.Common.Errors;

public sealed class Book { /* AIP-122 resource entity */ }

throw SchemataResourceErrors.PermissionDenied<Book>(
    name: "books/x",
    owner: "users/alice",
    description: "Permission 'book.Update' denied on resource 'books/x'.",
    reason: "BOOK_UPDATE_DENIED");
```

### Exception types

| Exception                     | HTTP code | Canonical status      | Default message                                               |
| ----------------------------- | --------- | --------------------- | ------------------------------------------------------------- |
| `InvalidArgumentException`    | 400       | `INVALID_ARGUMENT`    | The request contains an invalid argument.                     |
| `ValidationException`         | 422       | `INVALID_ARGUMENT`    | One or more validation errors occurred.                       |
| `NotFoundException`           | 404       | `NOT_FOUND`           | The requested resource was not found.                         |
| `AlreadyExistsException`      | 409       | `ALREADY_EXISTS`      | The resource already exists.                                  |
| `PermissionDeniedException`   | 403       | `PERMISSION_DENIED`   | You do not have permission to perform this action.            |
| `UnauthenticatedException`    | 401       | `UNAUTHENTICATED`     | The request does not have valid authentication credentials.   |
| `AbortedException`            | 409       | `ABORTED`             | A concurrency conflict occurred while saving to the database. |
| `FailedPreconditionException` | 412       | `FAILED_PRECONDITION` | The request cannot be executed in the current system state.   |
| `TenantResolveException`      | 400       | `FAILED_PRECONDITION` | Unable to resolve tenant for the current request.             |
| `QuotaExceededException`      | 429       | `RESOURCE_EXHAUSTED`  | Rate limit exceeded.                                          |
| `NoContentException`          | 204       | `OK`                  | _(none)_                                                      |

Rate-limit policies can expose `QuotaMetadata.Subject` on their rejected lease to identify the
actual quota partition. Otherwise Core uses the authenticated `sub`, name identifier, or name,
then client IP; the fallback includes the current tenant identity when present. Lease
`MetadataName.RetryAfter` becomes `RetryInfoDetail` and the HTTP transport emits ceiling-seconds
`Retry-After`. Application `OnRejected` runs first; a started response remains application-owned.

`ValidationException` takes a set of `ErrorFieldViolation` values and wraps them in a
`BadRequestDetail`. `NoContentException` signals a successful body-less response, used by
validate-only requests.

## Detail types

| Detail class                | `@type` URL                          | Carries                                                                        |
| --------------------------- | ------------------------------------ | ------------------------------------------------------------------------------ |
| `BadRequestDetail`          | `.../google.rpc.BadRequest`          | `field_violations`: `List<ErrorFieldViolation>`                                |
| `ErrorInfoDetail`           | `.../google.rpc.ErrorInfo`           | `reason`, `domain`, `metadata`                                                 |
| `LocalizedMessageDetail`    | `.../google.rpc.LocalizedMessage`    | `locale`, `message`                                                            |
| `PreconditionFailureDetail` | `.../google.rpc.PreconditionFailure` | `violations`: `List<PreconditionViolation>` (`type`, `subject`, `description`) |
| `QuotaFailureDetail`        | `.../google.rpc.QuotaFailure`        | `violations`: `List<QuotaViolation>` (`subject`, `description`)                |
| `RequestInfoDetail`         | `.../google.rpc.RequestInfo`         | `request_id`, `serving_data`                                                   |
| `ResourceInfoDetail`        | `.../google.rpc.ResourceInfo`        | `resource_type`, `resource_name`, `owner`, `description`                       |

Each `ErrorFieldViolation` carries `field` (snake_case path), `description`, and `reason`. With
FluentValidation, the reason is derived from the validator's error code by stripping the
`Validator` suffix and converting to AIP-193 UPPER_SNAKE_CASE (e.g. `NOT_EMPTY`, `INCLUSIVE_BETWEEN`).
Comparison operands ride inside `description` via the FluentValidation message template; `reason`
carries only the validator code. `ErrorInfoDetail.Reason` uses values such as
`SchemataConstants.ErrorReasons.ConcurrencyMismatch` (`"CONCURRENCY_MISMATCH"`) or any AIP-193 key
matching an entry in `SchemataResources.resx`.

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

- `ValidationException` uses HTTP 422 with `INVALID_ARGUMENT`. [`google.rpc.Code`](https://github.com/googleapis/googleapis/blob/master/google/rpc/code.proto)
  maps `INVALID_ARGUMENT` to HTTP 400; Schemata separates validation failures from malformed
  requests.
- `FailedPreconditionException` uses HTTP 412 with `FAILED_PRECONDITION`. `google.rpc.Code` maps
  `FAILED_PRECONDITION` to HTTP 400, which `TenantResolveException` uses. The classes have separate
  defaults, and their source does not state why they differ.
- `AbortedException` uses HTTP 409 with status `ABORTED`, matching the `google.rpc.Code` table.
- `NoContentException` uses HTTP 204 with `OK`; `google.rpc.Code` maps `OK` to HTTP 200. It is a
  control-flow signal for validate-only requests, and its `CreateErrorResponse` returns `null` so
  the response has no body.
- `OAuthException` returns an RFC 6749 `OAuthErrorResponse`; its `error` value is an OAuth error
  code rather than a `google.rpc.Code` name.

## See also

- [JSON Serialization](json-serialization.md) — how `IErrorDetail` and `@type` are serialized
- [Built-in Features](built-in-features.md) — `SchemataTransportHttpFeature` (410M)
