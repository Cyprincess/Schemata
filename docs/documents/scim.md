# SCIM provisioning protocol

`Schemata.Scim` provides the shared HTTP protocol, schema, filtering, projection, PATCH, discovery, and provisioning-binding infrastructure for RFC 7643 and RFC 7644. Domain packages register resource handlers that map their own entities and commit their own writes. The supported profile is limited to the behavior below; it is not a claim of interoperability with every SCIM client.

The package targets `net8.0` and `net10.0`. SCIM is exposed over HTTP with `application/scim+json`; there is no SCIM gRPC service or protocol mapping.

## Registration and ownership

`AddSchemataScim()` registers the protocol controllers, service-provider options, SCIM model-state errors, media-type errors, and `IScimBindingStore`. `UseSchemataScimErrors()` installs SCIM error conversion and request-body limits. Register the middleware before `UseRouting()` and map controllers through the host's normal ASP.NET Core pipeline.

Each domain registers an `IScimResourceHandler`. The shared controller and `ScimSchemaRegistry` resolve `IEnumerable<IScimResourceHandler>` and use only those handlers for resource routes and discovery. The package does not inspect service descriptors, discover entity types, or search for Identity or Authorization registrations. A resource endpoint is available only when its handler is registered.

The handler contract carries:

| Member | Responsibility |
| --- | --- |
| `ResourceType` | Discovery name, id, endpoint, base schema, and extensions. |
| `Schemas` | Attribute metadata used by filters, projection, input sanitization, and PATCH. |
| `GetAsync` / `SearchAsync` | Mapped reads for one resource or a normalized query. |
| `CreateAsync` / `ReplaceAsync` / `PatchAsync` | Domain writes and resource mapping. A missing PUT or PATCH target returns `null`. |
| `DeleteAsync` | Domain deletion; `false` means the target was not found. |

`IScimProvisioningDomainProvider.GetDomainAsync` resolves the caller's provisioning domain. `IScimProvisioningAuthorizer` supplies write authorization and the separate privileged-permissions decision. The shared controller calls `CanWriteAsync` for create, replace, patch, and delete when an authorizer is registered. Domain handlers use the domain provider and permission-specific decision where their provisioning rules require them.

## Identity Users

`UseIdentity<TUser, TRole>(...).UseScim()` registers `ScimUserHandler<TUser>` and `ScimGroupHandler<TUser, TRole>` for the entity types selected by that builder:

```csharp
schema.UseIdentity<SchemataUser, SchemataRole>().UseScim();
```

The host supplies an `IScimProvisioningDomainProvider` and registers `IRepository<ScimProvisioningBinding>` in the same provider context as its Identity entities. The User schema contains `schemas`, `id`, `externalId`, `meta`, `userName`, `displayName`, `active`, and read-only `groups`. `id` is the entity `Uid`, so renaming `userName` does not change resource identity. PUT returns `404` for a missing id and never creates it.

The handler provisions through `SchemataUserManager<TUser>`. Each create, replace, PATCH, or delete begins one serializable transaction, stages the user and binding writes, and commits once. Dependent user claims, logins, tokens, and role links use the existing manager deletion rules. A custom store must implement `IUserProvisioningStore<TUser>`; an incapable store fails before staging.

`externalId` is stored only in a binding keyed by `(resourceType, provisioningDomain, externalId)`. Different provisioning domains may use the same external id for different users. A duplicate external id in one domain returns `409 uniqueness`. User resolution uses `Uid`; matching `userName` or `displayName` never adopts an existing user. Writes against a user bound in another domain return `403` and leave its user and binding rows unchanged.

`active` maps to `AdministrativelyActive`. Changing it rotates the security stamp, while temporary `LockoutEnd` remains unchanged. Provisioning does not issue a cookie or token, confirm an email address, or accept a password. Password input is rejected without returning its value, and responses contain no password hash, security stamp, concurrency stamp, or token fields.

Usernames use RFC 8265 `UsernameCaseMapped` through the registered `ILookupNormalizer`. `UserManager.NormalizeName` is the public normalization entry. The original `UserName` is preserved, while `NormalizedUserName` stores the comparison key used by writes, duplicate detection, `FindByNameAsync`, and SCIM `userName` filters. Width mapping, lowercase mapping, NFC, contextual character rules, the RFC 5893 Bidi Rule, and space-separated userparts follow the checked-in Unicode 6.3 tables. Regenerate them from the pinned IANA and Unicode inputs with:

```shell
dotnet eng/precis.generate.cs
```

Canonical profile references: [RFC 8265 §3.3](https://www.rfc-editor.org/rfc/rfc8265#section-3.3), [RFC 8264 §7](https://www.rfc-editor.org/rfc/rfc8264#section-7), and [RFC 5893 §2](https://www.rfc-editor.org/rfc/rfc5893#section-2).

## Identity Groups

`ScimGroupHandler<TUser, TRole>` maps the selected role type to `schemas`, `id`, `externalId`, `meta`, `displayName`, and direct User `members`. Group ids use the role `Uid`. A role is visible and writable through Groups only when its `Group` provisioning binding belongs to the caller's domain. Writes to an unbound role return `403`; reads omit it and return `404` by id. A matching `displayName` never adopts an unbound role.

Create checks `IScimProvisioningAuthorizer.CanWritePermissionsAsync` in addition to the shared write check. The new role has no role claims or client-supplied permission state. `displayName` changes update the role's normalized lookup value while its `Uid`, resource `Name`, and canonical name remain stable.

Every member must be an existing User with a binding in the same domain and repository scope. Missing and cross-domain Users, nested Groups, and inconsistent `value`, `$ref`, or `type` input return `400 invalidValue`. `$ref` accepts an absolute URI or absolute path ending in `/Users/{value}` and is checked only as syntax. Duplicate member ids are idempotent.

PUT replaces the complete `displayName`, `externalId`, and membership set. Omitted `members` clears membership, and omitted `externalId` leaves an ownership binding with a null external id. PATCH uses the shared add, remove, replace, and `members[value eq "..."]` behavior. The read-only User `groups` attribute projects direct Groups bound to the caller's domain.

Each create, replace, PATCH, or delete begins one serializable transaction through `IRoleProvisioningStore<TRole>`. `SchemataRoleManager<TRole>` stages role writes, `SchemataUserManager<TUser>` stages membership writes, and the binding store joins the same unit of work. The handler commits once; a later role, membership, or binding failure rolls back every staged change. Independent manager and store APIs retain their own transaction ownership. Delete also removes dependent membership and role-claim rows through the role manager's deletion path.

## Authorization Applications

`UseAuthorization(...).UseScim()` registers `ScimApplicationHandler<TApp>` for the Application type selected by the authorization builder:

```csharp
schema.UseAuthorization(options => options.Issuer = "https://auth.example.com")
      .UseAuthorizationCodeFlow()
      .UseScim();
```

The host registers `IRepository<ScimProvisioningBinding>` in the same provider context as the Application and security repositories. The handler uses the shared authenticated provisioning-domain and write-authorization services described above.

Applications are available at `/scim/v2/Applications` with these project-owned schema URIs:

- `https://github.com/Cyprincess/Schemata/scim/schemas/2.0/Application`
- `https://github.com/Cyprincess/Schemata/scim/schemas/2.0/ApplicationAuthorization`

SCIM is the privileged provisioning surface. Dynamic client registration remains the OAuth client-driven protocol with initial-access and registration-access tokens. Both paths call `RegistrationMetadataMapper` and `SchemataApplicationManager`; the raw `Authorization.Http` and `Authorization.Grpc` management transports are removed.

The Application schema exposes public registration metadata in camelCase. `grantTypes`, `responseTypes`, and `scope` populate the typed fields enforced by authorization and token handlers. They never create `Permissions` entries. `clientId` is generated on create and remains immutable. SCIM `id` is the stable entity `Uid`; the consumer's repository add advisor supplies the independent resource `Name`.

`externalId` uses the same provisioning binding as Users and Groups. Its uniqueness scope is `(Application, provisioningDomain, externalId)`. Reusing an external id in another domain is valid, reusing it for another Application in one domain returns `409 uniqueness`, and writes against an Application bound to another domain return `403`.

The ApplicationAuthorization extension contains only `permissions`. An ordinary write preserves the existing collection, including when PUT omits the extension. A changed collection requires `IScimProvisioningAuthorizer.CanWritePermissionsAsync`; rejection returns `403` before any write. A delegated caller can replace or clear the collection through the extension.

`jwks` is a top-level multi-valued complex attribute whose entries contain public JWK parameters. The mapper converts it to the native OAuth `{"keys":[...]}` document. Inline keys and `jwksUri` remain mutually exclusive, and private or symmetric key material is rejected. Responses omit client secrets, private keys, registration access tokens, registration management URIs, and OAuth tokens. Credential issuance and rotation remain in the registration and security-management paths.

Every create, replace, PATCH, and delete starts one serializable transaction through `IApplicationManager<TApp>.BeginProvisioning()`. Application, public security metadata, and binding writes join that transaction and commit once. A later failure rolls back all three. Delete uses `ApplicationResourceMutation` through the manager, so the existing cascade removes dependent tokens, grants, pairwise mappings, credentials, and client key rows.

## Configuration

`ScimServiceProviderOptions` supplies these defaults:

| Option | Default | Behavior |
| --- | --- | --- |
| `BasePath` | `/scim/v2` | Controller routes, error handling, and generated locations use this root. |
| `DefaultPageSize` | `100` | Used when GET or POST `.search` omits `count`. |
| `MaxPageSize` | `200` | Caps every page size and supplies `filter.maxResults` in discovery. |
| `MaxRequestBodySize` | `1048576` bytes | Requests with a larger body receive `413` before a handler runs. |
| `MaxFilterLength` | `8192` UTF-8 bytes | Longer filters receive `400 invalidFilter`. |
| `MaxFilterDepth` | `32` | Deeper filter nesting receives `400 invalidFilter`. |
| `MaxPatchOperations` | `100` | Larger PATCH documents receive `400 invalidValue` before handler execution. |
| `DocumentationUri` | `null` | Optional ServiceProviderConfig documentation link. |
| `AuthenticationSchemes` | empty | Authentication scheme descriptions served in ServiceProviderConfig. |

## HTTP surface

The Identity handlers expose these routes with `<collection>` set to `/scim/v2/Users` or `/scim/v2/Groups`:

| Operation | Route | Success |
| --- | --- | --- |
| Create | `POST <collection>` | `201` with the mapped resource and `Location`. |
| List | `GET <collection>` | `200 ListResponse`, including an empty `Resources` array. |
| Search | `POST <collection>/.search` | `200 ListResponse`; the body uses the SearchRequest schema. |
| Retrieve | `GET <collection>/{id}` | `200` with `Content-Location`, or `404`. |
| Replace | `PUT <collection>/{id}` | `200`, or `404` without creating a resource. |
| Patch | `PATCH <collection>/{id}` | `200`, or `404`. |
| Delete | `DELETE <collection>/{id}` | `204`, or `404`. |

Discovery serves `GET /ServiceProviderConfig`, `/ResourceTypes`, `/ResourceTypes/{id}`, `/Schemas`, and `/Schemas/{id}` below the configured root. Discovery collections are ListResponses. Single resources have `meta.location` and a matching `Content-Location`; create also sets `Location`.

Resource endpoints require an authenticated `ClaimsPrincipal`. Anonymous calls receive `401`. A registered authorizer can deny a write with `403`. Domain and protocol failures use the SCIM Error schema rather than the AIP-193 envelope.

## Queries, filters, and projection

`startIndex` is one-based. Values below `1` become `1`; an index beyond the result set returns an empty page. `count` is capped by `MaxPageSize`, negative values become `0`, and `0` returns `totalResults` with no resources. `itemsPerPage` is the number of resources in the returned page.

`ScimFilterParser` produces a typed, schema-resolved AST for:

- `eq`, `ne`, `co`, `sw`, `ew`, `pr`, `gt`, `ge`, `lt`, and `le`;
- `and`, `or`, and `not`, with parentheses and RFC precedence;
- complex attributes such as `name.givenName` and `meta.lastModified`;
- multi-valued `valuePath` expressions such as `emails[type eq "work" and value co "@example.com"]`;
- schema-qualified attributes such as `urn:ietf:params:scim:schemas:core:2.0:User:userName`.

Attribute names and operators are case-insensitive. String comparison follows `caseExact`; dates compare chronologically and numbers compare numerically. Boolean and binary attributes reject relational operators with `400 invalidFilter`. Unknown attributes, invalid literal types, unsupported operators, malformed JSON strings, unclosed groups, and excess depth also produce `invalidFilter`. Extension schema URIs and their JSON object keys use exact ordinal matching; ordinary attribute names inside those objects remain case-insensitive.

Handlers receive the normalized `ScimSearchRequest`, including its parsed `FilterExpression`, and apply filtering in their domain query or mapping path. `ScimFilterEvaluator` evaluates the same AST against a mapped `JsonObject`.

Projection runs after domain mapping. `attributes` overrides the default set and retains attributes marked `always`. `excludedAttributes` removes selected defaults but cannot remove `always` attributes. The parameters are mutually exclusive. Complex paths can select one sub-attribute. Attributes marked `never`, including password-like fields and `meta.version`, are not returned. Extension attributes use their exact URI keys in projected output.

Relationship `$ref` values are populated during domain mapping before filter evaluation, PATCH
selection, and output projection. `ScimResults.ReferenceLocation` resolves the registered target
endpoint and uses the current request's scheme, host and `PathBase` with the configured SCIM
`BasePath`. Application JWK values preserve the standard `key_ops` member through
GET and replacement. Application security metadata is stored and projected by the common registration
mapper; installed registration request advisors validate configured JAR algorithm requirements.
Provisioning retains JAR policy when the reader feature is absent; installing
`UseJwtSecuredAuthorizationRequests()` activates request-object processing and algorithm allow-lists.

## Write and PATCH behavior

Create and PUT bodies are sanitized with the handler's schemas. Unknown optional attributes and `readOnly` input are ignored. Server-owned `id` and `meta` therefore cannot replace the corresponding mapped values. Writable and creation-time immutable attributes remain available to the domain handler. A missing PUT target stays a `404`; PUT does not create.

The `password` attribute is outside the supported provisioning schema. Password input at any nested location returns `400 invalidValue` with the detail `The password attribute is not supported.` The response and middleware log message do not include the supplied value.

`ScimPatchProcessor` validates the PatchOp schema and operation count, clones the mapped resource, and applies operations in request order. A failure leaves the input representation unchanged. Domain handlers apply the resulting representation inside their outer unit of work so a later domain or binding failure can roll back the complete write.

Supported PATCH behavior includes:

- root and attribute `add`, `remove`, and `replace`;
- simple, complex, and multi-valued attributes;
- sub-attribute paths and selected complex values;
- membership selections such as `members[value eq "..."]`;
- idempotent duplicate array additions and replacement of a missing simple value;
- `noTarget` for a missing remove path or an unmatched replacement selection;
- `mutability` for read-only changes, immutable changes, and required removals;
- `primary: true` resetting the other values of that multi-valued attribute.
- `invalidValue` for a nonboolean `primary`, with case-insensitive schema lookup and unchanged input.

Array indexing, JSON Patch `move`, and arbitrary extension operations are not supported.

## Provisioning bindings

`ScimProvisioningBinding` persists exactly four fields:

| Field | Purpose |
| --- | --- |
| `ResourceType` | The provisioning resource type. |
| `EntityUid` | The Schemata entity identifier. |
| `ProvisioningDomain` | The external provisioning administrative domain. |
| `ExternalId` | The optional client-issued identifier within that domain; ownership-only Group bindings use null. |

`FindByExternalIdAsync` resolves `(resourceType, provisioningDomain, externalId)`. `FindByEntityUidAsync` resolves `(resourceType, entityUid, provisioningDomain)`. `ListByEntityUidAsync` lists every domain binding for an entity so domain handlers can reject cross-domain writes before staging. A unique index covers the external-id triple and the primary key covers the entity triple.

`StageAsync`, `UpdateAsync`, and `RemoveAsync` call `IRepository.Join` with the caller's `IUnitOfWork` and stage through the repository. They never begin or commit a transaction. The domain handler commits entity, membership, and binding changes once; a `NoWrite` repository result is an error rather than a successful binding operation.

## Discovery and unsupported capabilities

ServiceProviderConfig advertises `patch.supported: true` and `filter.supported: true`, with `filter.maxResults` set to `MaxPageSize`. The remaining declarations are:

| Capability | Declaration or response |
| --- | --- |
| Bulk | `bulk.supported: false`; both bulk limits are `0`. `POST /Bulk` returns a SCIM `501` error. |
| Sort | `sort.supported: false`; a supplied `sortBy` returns `501`. |
| Password change | `changePassword.supported: false`; no change-password route is installed. |
| ETag | `etag.supported: false`; no ETag is generated and `meta.version` is omitted. |
| Root cross-type search | `POST /.search` returns a SCIM `501` error. |
| `/Me` | Returns a SCIM `501` error. |
| Enterprise User | Not registered or advertised. |
| Cursor pagination | Not implemented; only `startIndex` pagination is available. |
| Security events | No SCIM security-event endpoint or schema is installed. |
| gRPC | No SCIM gRPC surface. |

## Error shape

Errors contain exactly the Error schema, a string `status`, optional `scimType`, and optional `detail`:

| Status | `scimType` | Conditions |
| --- | --- | --- |
| `400` | `invalidSyntax` | Invalid JSON, missing SearchRequest or PatchOp schema, or invalid PATCH operation syntax. |
| `400` | `invalidFilter` | Invalid filter grammar, path filter, type/operator pair, length, or depth. |
| `400` | `invalidPath` | Invalid PATCH attribute path. |
| `400` | `invalidValue` | Invalid parameter values, mutually exclusive projections, or password input. |
| `400` | `noTarget` | Missing remove path or unmatched replacement selection. |
| `400` | `mutability` | Read-only, immutable, or required-attribute violation. |
| `400` | `invalidValue` | PATCH operation limit exceeded. |
| `401`, `403`, `404`, `405`, `501` | omitted | Authentication, authorization, missing route/resource, method, or unsupported capability. |
| `409` | `uniqueness` when supplied by the domain | Domain uniqueness conflict. |
| `413` | omitted | Request body limit exceeded. |
| `415` | `invalidSyntax` | Unsupported request media type. |
| `500` | omitted | Unhandled failure; generic errors use a non-sensitive detail. |

## Executable evidence hosts and provider switching

The integration hosts register both persistence providers and select one through `ASPNETCORE_ENVIRONMENT`. They use local shared-cache in-memory SQLite, consumer `IRepositoryAddAdvisor<>` registrations that supply resource `Name`, derived `ProviderRole` and `ProviderApplication` entities, and fresh service scopes for native-manager readback.

| Environment | Identity provider | Authorization provider | Selected derived entities |
| --- | --- | --- | --- |
| `ProviderEfCore` | `EfCoreRepository<ProviderIdentityDbContext,TEntity>` | `EfCoreRepository<AuthorizationScimDbContext,TEntity>` | `ProviderRole`, `ProviderApplication` |
| `ProviderLinqToDb` | `LinqToDbRepository<IdentityScimConnection,TEntity>` | `LinqToDbRepository<AuthorizationScimConnection,TEntity>` | `ProviderRole`, `ProviderApplication` |

The provider projects are referenced by both test projects:

```xml
<ProjectReference Include="..\..\src\Schemata.Entity.EntityFrameworkCore\Schemata.Entity.EntityFrameworkCore.csproj" />
<ProjectReference Include="..\..\src\Schemata.Entity.LinqToDB\Schemata.Entity.LinqToDB.csproj" />
```

Build the solution, then start either pair of real Kestrel hosts. The following commands use the net8 output; substitute `net10.0` to switch the host target framework.

```shell
dotnet build Schemata.slnx -c Release /p:TreatWarningsAsErrors=true
dotnet artifacts/bin/Schemata.Identity.Scim.Integration.Tests/Release/net8.0/Schemata.Identity.Scim.Integration.Tests.dll --urls http://127.0.0.1:5181 --environment ProviderEfCore
dotnet artifacts/bin/Schemata.Authorization.Scim.Integration.Tests/Release/net8.0/Schemata.Authorization.Scim.Integration.Tests.dll --urls http://127.0.0.1:5182 --environment ProviderEfCore
```

Change both `--environment` values to `ProviderLinqToDb` and use fresh host processes to execute the other provider. Each process creates a new database. Stop one provider pair before starting the next on the same ports.

`docs/evidence/scim/provisioning-collection.json` accepts the two base URLs as collection variables. Install Newman only in a temporary workspace and run the actual external client against these hosts:

```shell
npm install --prefix <temp-workspace> newman@6.2.1 --no-audit --no-fund
node <temp-workspace>/node_modules/newman/bin/newman.js run docs/evidence/scim/provisioning-collection.json --env-var identityBaseUrl=http://127.0.0.1:5181/scim/v2 --env-var authorizationBaseUrl=http://127.0.0.1:5182/scim/v2
```

The sanitized transcript records both provider runs, 17 successful requests and assertions per run, and the actual client, runtime, and provider versions. The test-only `X-Test-Scim`, `X-Test-Domain`, and `X-Test-Permissions` headers exercise the host's authentication and delegated-permission seams; production hosts supply their own authentication and authorization services.

## Management package cutover

`Identity.Http`, `Identity.Grpc`, `Authorization.Http`, and `Authorization.Grpc` have no replacement raw management routes. Use these owners instead:

- In-process User, Role, Application, Scope, Token, and security work uses the existing managers and stores.
- OAuth client-driven registration uses DCR. `UseDynamicClientRegistration()` installs create/read, while `UseRegistrationReplace()` and `UseRegistrationDelete()` install their named methods.
- Privileged SCIM provisioning uses `UseIdentity<TUser,TRole>().UseScim()` or `UseAuthorization(...).UseScim()` with the host-selected entities and a provisioning-domain provider.
- Application-owned resources use `Schemata.Resource.Http` and `Schemata.Resource.Grpc`, whose package identities and registration APIs are unchanged.

Migrate callers directly to the owning API. Do not add aliases, controller copies, compatibility shims, or a second management path. [Transport surfaces](transport-surfaces.md) records the operation-level ownership, executed evidence, limitations, and the later-change parity gate.

## Test evidence and references

`tests/Schemata.Scim.Tests/ScimProtocolShould.cs` covers HTTP CRUD, `.search`, pagination, authentication, uniqueness, projection, password redaction, unsupported capabilities, limits, and binding transaction behavior. `ScimFilterShould.cs` covers RFC grammar, precedence, case rules, valuePath, depth, projection, and exact extension keys. `ScimPatchShould.cs` covers ordered clone-based PATCH operations, selections, idempotence, limits, and mutability. `ScimDiscoveryShould.cs` and `ScimErrorShould.cs` cover discovery and protocol error responses.

`tests/Schemata.Identity.Scim.Integration.Tests/ScimUsersShould.cs` covers User lifecycle, normalization, privilege boundaries, and rollback. `ScimGroupsShould.cs` covers Group lifecycle, explicit role privilege, bound ownership, membership PATCH and PUT semantics, invalid and cross-domain members, read-only User groups, custom role selection, deletion cleanup, and fresh-scope rollback after the second membership write fails. `tests/Schemata.Authorization.Scim.Integration.Tests/ScimApplicationsShould.cs` covers Application discovery, lifecycle, immutable identifiers, delegated permissions, external-id scope, public JWK projection, credential redaction, OAuth metadata consumption, deletion cascade, and fresh-scope rollback after security or binding failures.

Both `ScimProviderLifecycleShould.cs` files parameterize EF Core and LinqToDB with derived entities, native-manager/SCIM state sharing, provider concurrency and rollback, domain-scoped external ids, filter/projection/pagination, deletion cleanup, and unsupported boundaries. The Application file also proves DCR, native manager, SCIM, and OAuth client-credentials consumption against one registration. These suites passed 41 Identity tests and 17 Authorization tests on each of net8.0 and net10.0 for this revision.

`docs/evidence/scim/provisioning-transcript.json` records Newman 6.2.1 on Node 26.13.0 against real net8.0 Kestrel hosts. EF Core 8.0.28 and LinqToDB 6.5.0 each completed all 17 collection requests and assertions with zero failures. The transcript removes authorization material and secret-bearing fields and limits its claim to this project-owned profile.

Canonical specifications:

- RFC 7643, *System for Cross-domain Identity Management: Core Schema*: <https://www.rfc-editor.org/rfc/rfc7643>
- RFC 7644, *System for Cross-domain Identity Management: Protocol*: <https://www.rfc-editor.org/rfc/rfc7644>
