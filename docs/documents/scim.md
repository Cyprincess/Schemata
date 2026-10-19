# SCIM provisioning

`Schemata.Scim` serves the RFC 7643 / RFC 7644 protocol surface over HTTP. `Schemata.Identity.Scim`
binds that surface to ASP.NET Core Identity users and roles. The implemented subset covers service
provider discovery, User and Group create/read/replace/delete, `ListResponse` pagination, SCIM error
bodies, and the attribute mutability declared by discovery. Optional SCIM capabilities that are not
implemented are advertised as unsupported and listed in [Unsupported surface](#unsupported-surface).

The SCIM surface is exposed over HTTP only. gRPC has no SCIM service, proto contract, or transport
mapping.

## Where the code lives

| Package                        | Key files                                                                                                                                     |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Scim`                | `Constants/ScimConstants.cs`, `Models/ScimResource.cs`, `Models/ListResponse.cs`, `Models/ServiceProviderConfig.cs`, `Models/ScimSchema.cs`, `Models/ScimSchemaRegistry.cs`, `Controllers/ScimDiscoveryController.cs`, `Controllers/ScimControllerBase.cs`, `ScimResults.cs`, `Middleware/ScimErrorMiddleware.cs`, `Extensions/ScimEndpointExtensions.cs` |
| `Schemata.Identity.Scim`       | `Features/SchemataIdentityScimFeature.cs`, `Controllers/ScimUsersController.cs`, `Controllers/ScimGroupsController.cs`, `Services/ScimUserAdapter.cs`, `Services/ScimGroupAdapter.cs` |

Both packages target `net8.0` and `net10.0`. The protocol base path is the fixed constant
`ScimConstants.BasePath` (`/scim/v2`) in `src/Schemata.Scim/Constants/ScimConstants.cs`; it is not
configurable.

## Enabling the adapter

`SchemataIdentityScimFeature<TUser>` depends on `SchemataIdentityFeature` and
`SchemataTransportHttpFeature`, and `UseIdentityScim<TUser>()` registers it:

```csharp
builder.UseSchemata(schema => {
    schema.UseIdentity();
    schema.UseIdentityScim<SchemataUser>();
});
```

The feature registers the User resource type and schema, the Users controller, and the SCIM error
middleware through `ConfigureApplication`. Groups activate only when the host registers the default
`RoleManager<SchemataRole>`, the default role, role-claim, and user-role repositories, and
`IResourceMutation<SchemataRole>`; with a custom role type the Groups controller is removed from MVC
and the Group resource type and schema are absent from discovery.

### Required resource `Name` advisor

Both adapters add their entity without a `Name`: `ScimUserAdapter.CreateAsync` constructs
`new TUser { UserName = ..., DisplayName = ... }` and `ScimGroupAdapter.CreateAsync` constructs
`new SchemataRole { DisplayName = ... }`. Every repository add runs the registered
`AdviceAddCanonicalName<TEntity>`, which derives `CanonicalName` from the entity's resource-name
pattern and throws `ValidationException` when a placeholder such as `{name}` has no value. A host
that enables either adapter registers an add advisor that supplies `Name`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

internal sealed class ResourceNameAdvisor<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        MutationOptions      options,
        CancellationToken    ct
    ) {
        if (entity is ICanonicalName named) {
            named.Name ??= $"resource-{Guid.NewGuid():n}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}

schema.Services.TryAddEnumerable(
    ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(ResourceNameAdvisor<>)));
```

The SCIM integration tests use this advisor from
`tests/Schemata.Identity.Scim.Integration.Tests/Fixtures/ResourceNameAdvisor.cs` and register it in
`tests/Schemata.Identity.Scim.Integration.Tests/Program.cs`. `Name` stays a host decision: the
framework stores the value the advisor writes.

`SchemataIdentityScimOptions` controls authorization and paging:

```csharp
schema.Configure<SchemataIdentityScimOptions>(options => {
    options.AuthorizationPolicy = "ScimProvisioning";
    options.DefaultPageSize     = 100;
    options.MaxPageSize         = 200;
});
```

| Option                | Default | Effect                                                                                     |
| --------------------- | ------- | ------------------------------------------------------------------------------------------ |
| `AuthorizationPolicy` | `null`  | Names the policy evaluated for SCIM resources. When unset, the host's default policy runs. |
| `DefaultPageSize`     | 100     | `count` used by a list request that omits the parameter.                                    |
| `MaxPageSize`         | 200     | Upper bound applied to every list request, including an omitted `count`.                    |

`ScimUsersAuthorizationFilter` guards every Users and Groups action. It runs before model binding and
returns `401` for an unauthenticated principal and `403` when the policy fails or the configured
policy name is not registered, each with a SCIM error body. The discovery controller carries no SCIM
authorization filter; in a host without a global fallback policy the discovery endpoints answer
anonymous requests.

## Discovery

`ScimDiscoveryController` serves the discovery endpoints with `application/scim+json`. Resource
collections use the `ListResponse` message schema. Each returned resource carries
`meta.resourceType` and an absolute `meta.location`, and single-resource responses mirror
`meta.location` into `Content-Location`. `meta.version` is never emitted.

| Endpoint                            | Response                                                                                                             |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `GET /scim/v2/ServiceProviderConfig` | One `ServiceProviderConfig` with `schemas`, `meta`, optional `documentationUri`, and `authenticationSchemes`.          |
| `GET /scim/v2/ResourceTypes`         | `ListResponse` of installed resource types.                                                                            |
| `GET /scim/v2/ResourceTypes/{id}`    | One resource type, resolved by id or name.                                                                            |
| `GET /scim/v2/Schemas`               | `ListResponse` of the three discovery schemas plus the schemas contributed by resource adapters.                       |
| `GET /scim/v2/Schemas/{id}`          | One schema by URI.                                                                                                    |

The served ServiceProviderConfig declares these capabilities:

| Capability       | `supported` | Size fields                          |
| ---------------- | ----------- | ------------------------------------ |
| `patch`          | `false`     |                                      |
| `bulk`           | `false`     | `maxOperations: 0`, `maxPayloadSize: 0` |
| `filter`         | `false`     | `maxResults: 0`                      |
| `changePassword` | `false`     |                                      |
| `sort`           | `false`     |                                      |
| `etag`           | `false`     |                                      |

`documentationUri` and `authenticationSchemes` come from the host through
`ScimServiceProviderOptions`. The installed resource types are `User` (`/Users`,
`urn:ietf:params:scim:schemas:core:2.0:User`) and, when the default role services are present,
`Group` (`/Groups`, `urn:ietf:params:scim:schemas:core:2.0:Group`). No other resource type or schema
extension is registered.

## User resource

`ScimUsersController` serves `POST /scim/v2/Users`, `GET /scim/v2/Users`,
`GET/PUT/DELETE /scim/v2/Users/{id}`, and a PATCH action. The adapter in `ScimUserAdapter.cs` maps
`SchemataUser` (or a custom `TUser`) through `SchemataUserManager<TUser>` and `IRepository<TUser>`.

| Attribute     | Type      | Mutability  | Returned | Uniqueness | Behavior                                                                                                            |
| ------------- | --------- | ----------- | -------- | ---------- | ------------------------------------------------------------------------------------------------------------------- |
| `userName`    | `string`  | `readWrite` | `default` | `server`   | Required on POST and PUT. A missing or whitespace-only value returns `400 invalidValue`. Uniqueness is case-insensitive through Identity normalization. |
| `displayName` | `string`  | `readWrite` | `default` | `none`     | Stored on `SchemataUser.DisplayName`. PUT replaces it; an omitted value clears it.                                    |
| `active`      | `boolean` | `readWrite` | `default` | `none`     | Administrative enable flag. POST defaults to `true`. PUT applies the value only when supplied.                        |
| `password`    | `string`  | `writeOnly` | `never`   | `none`     | Accepted on POST and PUT, validated by the configured password validators, hashed by the configured hasher, and never returned. |
| `groups`      | `complex` | `readOnly`  | `default` | `none`     | Multi-valued. Derived from SCIM-managed group memberships. Client input is ignored.                                  |

Common attributes (`id`, `schemas`, `meta`) follow RFC 7643 §3.1. `id` is the user's `Uid` and stays
unchanged across rename and replace. `meta` carries `resourceType`, `location`, `created`, and
`lastModified`; `meta.version` is absent. Client-supplied `id`, `schemas`, `meta`, `groups`, and
any attribute not listed above are ignored. `externalId` is not stored or returned.

| Operation                    | Success       | Notes                                                                                                                  |
| ---------------------------- | ------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `POST /Users`                | `201 Created` | Body is the created User. `Location` and `Content-Location` equal `meta.location`.                                      |
| `GET /Users/{id}`            | `200 OK`      | User body with `Content-Location`. `404` when the id is not a user `Uid`.                                                |
| `GET /Users`                 | `200 OK`      | `ListResponse`.                                                                                                         |
| `PUT /Users/{id}`            | `200 OK`      | Full replacement of the writable attributes. `404` when missing; PUT never creates.                                      |
| `DELETE /Users/{id}`         | `204 No Content` | Deletes through `UserManager`, including the Identity dependent-row cleanup. `404` when missing.                     |
| `PATCH /Users/{id}`          | `501 Not Implemented` | SCIM error body.                                                                                                 |

Write semantics:

- POST uses `UserManager.CreateAsync`, so user-name validation, normalization, uniqueness, and password rules are the Identity admission path.
- PUT requires `userName`; it replaces `displayName`, applies `active` when supplied, and sets a new password only when `password` is supplied. Omitting `password` leaves the current hash unchanged.
- PUT rotates `SecurityStamp` when the user name changes and when `active` changes, so existing sessions cannot outlive the change.
- Duplicate user names, compared case-insensitively, return `409 uniqueness` on POST and PUT. Uniqueness is enforced by the Identity user validator; the SCIM adapter adds no database unique constraint, so a concurrent create that passes validation before either row commits can still collide.
- `active` is the administrative sentinel `LockoutEnd == DateTimeOffset.MaxValue` with lockout enabled. `active: false` sets the sentinel; `active: true` clears `LockoutEnd`. A temporary lockout reports `active: true`. Deprovisioning overrides a temporary lockout, and reactivation clears the lockout end entirely.
- Password rules come from the Identity password validators. A rejected password returns `400 invalidValue` with the validator descriptions joined into `detail`.

## Group resource

`ScimGroupsController` serves the same operation set under `/scim/v2/Groups`. `ScimGroupAdapter.cs`
maps a `SchemataRole` to a Group and `SchemataUserRole` rows to `members`, using
`RoleManager<SchemataRole>`, the role/role-claim/user-role repositories, and
`IResourceMutation<SchemataRole>`.

| Attribute     | Type      | Mutability  | Returned | Uniqueness | Behavior                                                                                       |
| ------------- | --------- | ----------- | -------- | ---------- | ---------------------------------------------------------------------------------------------- |
| `displayName` | `string`  | `readWrite` | `default` | `server`   | Required. Maps to the role display name; `NormalizedName` is recomputed and role validators run. Identity rejects a duplicate normalized role name with `409 uniqueness`. |
| `members`     | `complex` | `readWrite` | `default` | `none`     | Multi-valued. `value` is a user `Uid`; `type` accepts only `User`; `$ref` must end with `/Users/{value}`. |

`members` sub-attributes are `value` (`immutable`), `$ref` (`immutable`, `referenceTypes: ["User"]`),
`type` (`immutable`, canonical value `User`), and `display` (`readOnly`). Member `$ref` values are
validated as strings and are not fetched. Duplicate member values are idempotent. Nested groups are
rejected with `400 invalidValue`.

| Operation                     | Success          | Notes                                                                                                            |
| ----------------------------- | ---------------- | ---------------------------------------------------------------------------------------------------------------- |
| `POST /Groups`                | `201 Created`    | Creates the role, the ownership claim, and all membership links in one unit of work. `Location` and `Content-Location` are set. |
| `GET /Groups/{id}`            | `200 OK`         | Group body with members.                                                                                         |
| `GET /Groups`                 | `200 OK`         | `ListResponse`.                                                                                                  |
| `PUT /Groups/{id}`            | `200 OK`         | Replaces `displayName` and the complete managed membership in one unit of work. `404` when missing; PUT never creates. |
| `DELETE /Groups/{id}`         | `204 No Content` | Deletes the role through the role mutation owner, which removes membership links and role claims.                 |
| `PATCH /Groups/{id}`          | `501 Not Implemented` | SCIM error body.                                                                                            |

Membership semantics:

- PUT replaces the complete membership set. Members in the request but not stored are added; stored members absent from the request are removed; an omitted or empty `members` array clears every link.
- A failure while staging the role update or a membership change rolls back the role and membership rows in the same unit of work.
- Two concurrent PUT requests can interleave additions because user-role rows carry no version token; the result can be the union of both requests.

Provisioning ownership:

- A role participates in SCIM only when it carries the storage-only claim `urn:schemata:identity:scim:group` (`SchemataConstants.ProvisioningClaims.ScimGroup`). SCIM writes the claim on group creation. An administrator adopts an existing role by adding the claim through the role-claim store.
- Roles without the claim are absent from list results, return `404` from find/replace/delete, and cause a same-name POST to return `409 uniqueness`.
- The claim is filtered from `SchemataUserClaimsPrincipalFactory.GenerateClaimsAsync`, so cookies and tokens never carry the marker. The role row retains it for ownership checks.
- Client input never sets role claims, permissions, canonical names, or ids. Group `id` is the role `Uid`.

`User.groups` is read-only and lists SCIM-managed memberships only. Each entry carries the group `id` in `value`, the role display name in `display`, `type: "direct"`, and a controller-filled `$ref` to `/scim/v2/Groups/{id}`. Entries are sorted by group id. A host without group capability reports `groups: []`.

## ListResponse and pagination

List responses use the `urn:ietf:params:scim:api:messages:2.0:ListResponse` schema with
`totalResults`, `startIndex`, `itemsPerPage`, and `Resources`. `Resources` is always a JSON array,
including for an empty page.

| Parameter    | Behavior                                                                                                 |
| ------------ | -------------------------------------------------------------------------------------------------------- |
| `startIndex` | Defaults to 1. A value below 1 is treated as 1. The effective value is echoed in the response. A value beyond `totalResults` returns an empty page. |
| `count`      | Defaults to `DefaultPageSize` and is capped at `MaxPageSize`. A negative value is treated as 0. `0` returns `totalResults` with an empty page. |
| `filter`     | Any supplied value is rejected with `400 invalidFilter`.                                                  |
| `sortBy`, `sortOrder` | Not recognized by the actions and ignored.                                                     |
| `attributes`, `excludedAttributes` | Not implemented and ignored. The full default attribute set is returned.                       |

Users order by `Uid`; Groups order by role `Uid`. `itemsPerPage` is the number of resources in the page, and `totalResults` is the stored total, not the page size.

## Error responses

Protocol errors use the RFC 7644 §3.12 body:

```json
{
  "schemas": ["urn:ietf:params:scim:api:messages:2.0:Error"],
  "status": "400",
  "scimType": "invalidValue",
  "detail": "userName is required."
}
```

`status` is a JSON string. `scimType` is omitted when no RFC 7644 detail keyword applies. Every
current endpoint supplies `detail`. The `schemas` array contains exactly the single Error schema URI;
the AIP-193 `error` envelope is never used on the SCIM path.

| Status | `scimType`     | Trigger                                                                                                   |
| ------ | -------------- | --------------------------------------------------------------------------------------------------------- |
| 400    | `invalidValue` | Missing or blank `userName`/`displayName`, invalid member, password rejected by an Identity validator, Identity validation failure on a write. |
| 400    | `invalidSyntax` | Unparsable request body, or a request that fails MVC model binding under the SCIM path.                  |
| 400    | `invalidFilter` | A `filter` query parameter on a Users or Groups list.                                                    |
| 401    | none           | Unauthenticated principal on a resource endpoint.                                                         |
| 403    | none           | The configured policy is missing or denies the principal; a role/membership mutation is not permitted.     |
| 404    | none           | Unknown resource id, unknown discovery id, and unrouted SCIM paths.                                       |
| 405    | none           | A method not declared on a routed SCIM endpoint.                                                          |
| 409    | `uniqueness`   | Duplicate user name, duplicate role name, or a repository add/update advisor conflicts on create, replace, or delete. |
| 409    | none           | A repository mutation aborts the operation.                                                               |
| 415    | `invalidSyntax` | A request body media type has no SCIM input formatter.                                                     |
| 500    | none           | Unhandled exception. The detail is the framework's generic message unless a `SchemataException` supplies one. |
| 501    | none           | PATCH on a User or Group item.                                                                            |

`UseSchemataScimErrors` installs `ScimErrorMiddleware` for the whole SCIM path. It converts
unhandled exceptions (a `SchemataException` code in the 4xx/5xx range, otherwise 500) and empty
`404`/`405`/`415` responses into SCIM bodies. `AddSchemataScim` post-configures MVC model-state
handling so binding failures under `/scim/v2` return `400 invalidSyntax`, and registers
`ScimUnsupportedMediaTypeFilter`, which replaces MVC's unsupported-media-type result with the SCIM
error body before the client-error ProblemDetails response executes. Requests outside the SCIM path
keep the host's normal error handling.

## Unsupported surface

The following capabilities are absent by design and are declared unsupported where RFC 7643 provides
a discovery declaration. Observed responses come from the routing and controllers above.

| Capability                 | Requirement                                   | Declaration / observed behavior                                                                                     |
| -------------------------- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `PATCH`                    | RFC 7644 §3.5.2 (optional server function)    | `patch.supported: false`. Item PATCH returns `501`; collection PATCH returns `405`.                                  |
| Filtering                  | RFC 7644 §3.4.2.2                             | `filter.supported: false`, `maxResults: 0`. A `filter` parameter on a Users or Groups list returns `400 invalidFilter`. Discovery accepts and ignores `filter`. |
| Sorting                    | RFC 7644 §3.4.2.3 (optional)                  | `sort.supported: false`. `sortBy` and `sortOrder` are ignored.                                                       |
| Attribute projection       | RFC 7644 §3.4.2.5 (MUST support)              | Not implemented. `attributes` and `excludedAttributes` are ignored and the full default attribute set is returned. This is a deviation from the MUST requirement. |
| ETag and conditional requests | RFC 7644 §3.14 (optional)                  | `etag.supported: false`. No `ETag` header and no `meta.version`. `If-Match` is not evaluated; the write proceeds.    |
| Bulk                       | RFC 7644 §3.7 (optional)                      | `bulk.supported: false`, both limits 0. `/scim/v2/Bulk` is not routed and returns a `404` SCIM error.                |
| `/Me`                      | RFC 7644 §3.11                                | Not implemented. `/scim/v2/Me` is not routed and returns a `404` SCIM error; RFC 7644 suggests `501` for providers that do not support the alias. |
| `.search`                  | RFC 7644 §3.4.3                               | Not implemented. `GET /scim/v2/Users/.search` is treated as an unknown User id and returns `404`; `POST` returns `405`. |
| `changePassword`           | RFC 7643 §5                                    | `changePassword.supported: false`. No endpoint is routed.                                                            |
| Application resource       | RFC 7643 resource type defined by other specs | No `Application` resource type or schema. `/scim/v2/Applications` returns a `404` SCIM error.                        |
| Enterprise User extension  | RFC 7643 §4.3                                 | Not registered. No `schemaExtensions` entry and no enterprise attributes.                                            |
| `externalId`               | RFC 7643 §3.1                                 | Not stored, not returned, and ignored on input.                                                                      |
| Protocol version negotiation | RFC 7644 §3.13                              | The only base path is `/scim/v2`; no version negotiation or alternate version path.                                  |
| gRPC mapping               | none                                          | SCIM resources are HTTP-only.                                                                                         |

## Behavior limits

- Group creation writes the ownership claim with the role. Consumers that read role claims directly, for example through `RoleManager.GetClaimsAsync` or the role-claim repository, can observe the marker; it is internal provisioning metadata and is excluded from generated principals.
- `displayName` on the Group schema declares `uniqueness: server`, matching the `409 uniqueness` returned for a duplicate normalized role name.
- A custom-role Identity host serves Users only. Groups is not advertised, `/scim/v2/Groups` is not routed, and `User.groups` stays empty.
- Uniqueness of `userName` relies on the Identity user validator and normalized name. A database unique constraint is not part of the SCIM adapter.
- Group PUT has no version precondition, so concurrent replacements can produce a union of memberships.
- Discovery is filtered only by what is registered. It declares the installed subset rather than a conformance level.
- The base path is fixed at `/scim/v2`; `ScimConstants.BasePath` has no host override.

## Test evidence

| Area                         | Test class and facts                                                                                                     |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| Discovery media type, meta, capabilities, schema attributes | `ScimDiscoveryShould` in `tests/Schemata.Scim.Tests` (`Serve_Discovery_Endpoints_With_The_Scim_Media_Type`, `Declare_Unsupported_Optional_Capabilities`, `Serve_Every_Advertised_Service_Provider_Attribute_In_Its_Schema`, `Serve_Discovery_For_An_Empty_Registry`) |
| Discovery and protocol error bodies, `invalidSyntax`, omitted `scimType`, undeclared discovery methods, non-SCIM paths | `ScimDiscoveryShould.Convert_Protocol_Failures_To_Scim_Errors` and `ScimErrorShould` in `tests/Schemata.Scim.Tests` |
| Resource type and schema discovery | `ScimUsersShould.Register_The_User_Resource_Type_And_Schema_With_Discovery`, `ScimGroupsShould.Register_The_Group_Resource_Type_And_Schema_With_Discovery`, `ScimSurfaceShould.Declare_Only_The_Implemented_Resources_And_Capabilities`, `ScimSurfaceShould.Declare_User_And_Group_Attribute_Mutability` |
| Users CRUD, read-only input, password handling | `ScimUsersShould` (`Create_Get_List_Replace_And_Delete_A_User`, `Replace_Does_Not_Create_Missing_Users`, `Ignore_Read_Only_Input_And_Never_Return_The_Password`, `Keep_The_Server_Id_Immutable_Across_Rename_And_Rotate_The_Security_Stamp`) |
| Uniqueness, `active`, lockout | `ScimUsersShould.Reject_Empty_And_Case_Insensitively_Duplicate_User_Names`, `Map_Active_To_The_Administrative_Sentinel`, `Leave_A_Temporary_Lockout_Active_Until_Deprovisioning` |
| Pagination and filter rejection | `ScimUsersShould.Page_List_Responses_With_Clamped_Start_Index_And_Count`, `Cap_The_Page_Size_With_The_Configured_Maximum`, `Reject_A_Filter_Expression`, `ScimGroupsShould.Page_List_Groups_With_Clamped_Start_Index_And_Count` |
| Groups CRUD, membership, rollback | `ScimGroupsShould` (`Create_Get_List_Replace_And_Delete_A_Group`, `Require_A_Display_Name`, `Validate_Member_Value_Reference_And_Type`, `Replace_Clears_The_Complete_Membership`, `Roll_Back_A_Failed_Membership_Replacement`) |
| Group ownership and custom-role hosts | `ScimGroupsShould` (`Do_Not_Expose_Or_Adopt_Unmanaged_Roles`, `Adopt_An_Administratively_Marked_Role`, `Keep_The_Management_Marker_Out_Of_Principals`, `Keep_Groups_Off_A_Custom_Role_Host`) |
| Authorization boundary       | `ScimUsersShould.Reject_Unauthenticated_And_Unauthorized_Requests`, `ScimGroupsShould.Reject_Unauthenticated_And_Unauthorized_Group_Requests`, `ScimSurfaceShould.Require_Authentication_For_Resources_Only` |
| Operation matrix, unsupported endpoints, query parameters, ETag, media type | `ScimSurfaceShould` (`Serve_The_User_Operation_Matrix`, `Serve_The_Group_Operation_Matrix`, `Expose_No_Endpoints_For_Unsupported_Operations`, `Ignore_Unsupported_Collection_Parameters_And_Reject_Filters`, `Emit_No_Entity_Tags_And_Ignore_If_Match`, `Convert_Unsupported_Media_Types_To_Scim_Errors`) |

The surface matrix in [SCIM surface matrix](scim-surface-matrix.md) records each operation and the
fact that covers it.

## References

- RFC 7643, *System for Cross-domain Identity Management: Core Schema*: <https://www.rfc-editor.org/rfc/rfc7643>
- RFC 7644, *System for Cross-domain Identity Management: Protocol*: <https://www.rfc-editor.org/rfc/rfc7644>
- `src/Schemata.Scim/Constants/ScimConstants.cs`
- `src/Schemata.Scim/Models/ScimSchemaRegistry.cs`
- `src/Schemata.Identity.Scim/Services/ScimUserAdapter.cs`
- `src/Schemata.Identity.Scim/Services/ScimGroupAdapter.cs`
