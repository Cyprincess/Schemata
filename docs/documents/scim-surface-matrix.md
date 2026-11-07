# SCIM surface matrix

This matrix records the HTTP surface served by `Schemata.Scim`, `Schemata.Identity.Scim`, and
`Schemata.Authorization.Scim`. Protocol configuration and errors are in [SCIM provisioning](scim.md).
Executed evidence below is historical #127/#128 evidence, not a rerun against the final Task 9 tree.

## Common rules

| Item | Value |
| --- | --- |
| Base path | `/scim/v2` by default; `ScimServiceProviderOptions.BasePath` can replace it |
| Media type | `application/scim+json` on SCIM JSON responses |
| Resource `id` | Entity `Uid` as a GUID string; server assigned and stable across rename |
| `meta.location` | Absolute URI built from scheme, host, path base, base path, collection, and id |
| `Content-Location` | Mirrors `meta.location` for single-resource responses |
| `Location` | Set on successful `POST` create |
| ETag | Never emitted; `meta.version` is omitted |
| Resource authentication | `401` for an anonymous caller |
| Write authorization | `403` when the registered authorizer denies the operation |
| Unrouted SCIM path | `404` with the SCIM Error schema |
| Undeclared method | `405` with the SCIM Error schema when a route matches |

## Discovery

| Operation | Auth | Success | Evidence |
| --- | --- | --- | --- |
| `GET /scim/v2/ServiceProviderConfig` | none | `200 ServiceProviderConfig` | `ScimSurfaceShould.Discovery_Advertises_The_Identity_Protocol_Profile` |
| `GET /scim/v2/ResourceTypes` | none | `200 ListResponse` containing the registered User, Group, and/or Application types | `ScimProviderLifecycleShould` discovery assertions; handler set depends on host registration |
| `GET /scim/v2/ResourceTypes/{id}` | none | `200 ResourceType`, or `404` | `ScimDiscoveryShould` |
| `GET /scim/v2/Schemas` | none | `200 ListResponse` with registered discovery and resource schemas | `ScimProviderLifecycleShould`; Application URI ids are exercised through this collection |
| `GET /scim/v2/Schemas/{id}` | none | `200 Schema`, or `404` | `ScimSurfaceShould.User_Schema_Excludes_Secrets_And_Marks_Groups_ReadOnly` |

`patch.supported` and `filter.supported` are `true`. `bulk`, `sort`, `changePassword`, and `etag` are `false`; `filter.maxResults` is the configured maximum page size.

## Users

| Operation | Request attributes | Success | Errors |
| --- | --- | --- | --- |
| `POST /scim/v2/Users` | `userName` (required), `displayName`, `active`, `externalId` | `201 User` | `400`, `403`, `409 uniqueness`, `415`, `500` |
| `GET /scim/v2/Users` | Pagination, filter, and projection parameters | `200 ListResponse` | `400 invalidFilter`, `400 invalidValue`, `401`, `501` for sorting |
| `POST /scim/v2/Users/.search` | SearchRequest schema with the same query attributes | `200 ListResponse` | `400`, `401`, `501` for sorting |
| `GET /scim/v2/Users/{id}` | Optional projection parameters | `200 User` | `400`, `401`, `404` |
| `PUT /scim/v2/Users/{id}` | `userName` (required), `displayName`, `active`, `externalId` | `200 User` | `400`, `403`, `404`, `409`, `500` |
| `PATCH /scim/v2/Users/{id}` | PatchOp schema and ordered operations | `200 User` | `400`, `403`, `404`, `409`, `500` |
| `DELETE /scim/v2/Users/{id}` | none | `204` | `403`, `404`, `500` |

The response schema contains `schemas`, `id`, `externalId`, `meta`, `userName`, `displayName`, `active`, and read-only `groups`. Password input returns `400 invalidValue` without echoing the secret. Other secret, hash, stamp, and token fields are not mapped. Client-supplied `id`, `meta`, and `groups` are ignored.

Create, replace, PATCH, and delete stage Identity and binding changes in one serializable transaction. PUT does not create a missing user. `active` maps to `AdministrativelyActive` and leaves `LockoutEnd` unchanged. Lifecycle, stable-id, normalization, credential, and rollback evidence is in `ScimUsersShould`.

## Query behavior

| Parameter | Behavior |
| --- | --- |
| `startIndex` | Defaults to 1; lower values become 1; an index beyond the total returns an empty page |
| `count` | Defaults to 100, capped at 200; negative values become 0; 0 returns no resources with `totalResults` |
| `filter` | Typed RFC grammar with `userName` comparison through RFC 8265 normalization |
| `sortBy` | Returns `501`; sorting is unsupported |
| `attributes` | Selects response attributes after domain mapping |
| `excludedAttributes` | Excludes response attributes; mutually exclusive with `attributes` |
| Unknown parameter | Ignored by the shared controller |

`ListResponse` contains `schemas`, `totalResults`, `startIndex`, `itemsPerPage`, and `Resources`. Users are ordered by `Uid` before filtering and pagination.

## Unsupported routes

| Path | Response |
| --- | --- |
| `POST /scim/v2/Bulk` | `501` |
| `POST /scim/v2/.search` | `501` |
| `/scim/v2/Me` | `501` |
| `/scim/v2/Applications` | Registered by Authorization `UseScim()`; otherwise `404` |
| `/scim/v2/Groups` | Registered by Identity `UseScim()`; otherwise `404` |

Evidence for these routes and the authentication split is in `ScimSurfaceShould.Shared_Unsupported_Routes_And_Search_Use_The_Protocol_Contract` and `Authentication_And_Write_Authorization_Are_Independent`.

## Groups and Applications

| Resource | Registration and owner | Operations | Profile boundary |
| --- | --- | --- | --- |
| Groups | Identity `UseScim()`; `ScimGroupHandler<TUser,TRole>` and Identity managers | Create, read, list, search, replace, PATCH, delete | Direct User members and member valuePath updates; nested Groups rejected |
| Applications | Authorization `UseScim()`; `ScimApplicationHandler<TApp>`, application manager, metadata mapper | Create, read, list, search, replace, PATCH, delete | Public client metadata plus delegated permissions; credentials and tokens excluded |

Sources: `src/Schemata.Identity.Scim/Features/SchemataIdentityScimFeature.cs`,
`Handlers/ScimGroupHandler.cs`, `src/Schemata.Authorization.Scim/Features/SchemataAuthorizationScimFeature.cs`,
and `Handlers/ScimApplicationHandler.cs`. Canonical resource names remain consumer-assigned; SCIM ids
are UIDs. Native User/Role and Application administration uses managers, not a parallel Resource
management transport. External provisioning should use this SCIM profile.

## Error keywords

| Status | `scimType` | Source |
| --- | --- | --- |
| `400` | `invalidValue` | Invalid attributes, projections, or password input |
| `400` | `invalidSyntax` | Invalid JSON or required message schema |
| `400` | `invalidFilter` | Invalid filter grammar, type, depth, or length |
| `400` | `invalidPath` | Invalid PATCH attribute path |
| `400` | `noTarget` | Missing PATCH remove target or unmatched replacement |
| `400` | `mutability` | Read-only, immutable, or required-attribute change |
| `400` | `invalidValue` | PATCH operation limit exceeded |
| `401`, `403`, `404`, `405`, `500`, `501` | absent | Authentication, authorization, missing resource, method, failure, or unsupported operation |
| `409` | `uniqueness` | Duplicate normalized username or external id |
| `413` | absent | Request body limit exceeded |
| `415` | `invalidSyntax` | Unsupported request media type |

Every error body contains the Error schema, string `status`, and `detail`; `scimType` is present only where applicable. Protocol middleware redacts supplied passwords from responses and log messages.

## Executed provider and external-client evidence

`ScimProviderLifecycleShould` ran the User and Group profile against derived `ProviderRole`
entities on EF Core and LinqToDB SQLite. It checked discovery/schema casing and types, stable UIDs,
domain-scoped external ids, pagination/filter/projection, normalized username equality, member
valuePath updates, manager state sharing, rollback, deletion cleanup, and administrative admission.
Its concurrency case performs sequential stale native-manager updates from separate scopes;
it does not execute simultaneous SCIM HTTP writes or establish a SCIM conditional-write protocol.
Authorization's corresponding class ran derived `ProviderApplication` lifecycle, delegated
permissions, DCR/manager/SCIM sharing, client-credentials consumption, failed-write invariants,
and secret redaction. The recorded suites passed 41 Identity and 17 Authorization cases per TFM
on net8.0 and net10.0. These counts describe the recorded run, not current test discovery.

The unsupported-profile theory verifies password rejection without echo, nested-Group rejection, `501` for Bulk and sorting, `404` for Enterprise User and `/Events`, and no ETag. A supplied opaque `cursor` parameter has no cursor semantics; responses retain one-based `startIndex` pagination. Cursor pagination and security-event endpoints are not implemented.

`provisioning-collection.json` drives the same discovery, CRUD, filter/projection, and PATCH operations through Newman against real HTTP hosts. `provisioning-transcript.json` records 17 successful requests and assertions for each provider, actual Newman/Node/runtime/provider versions, sanitized request and response payloads, and limitations. This is evidence for the documented Schemata profile, not vendor-wide interoperability or certification.
