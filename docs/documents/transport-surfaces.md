# Transport surfaces

This matrix maps protocol and domain operations to shared owners. `HTTP` and `gRPC` describe remote
surfaces; `runtime-only` describes in-process APIs. Executed results record the #127/#128 evidence
run, not a rerun against the final Task 9 tree. A suite linked without a result remains unexecuted
for that evidence run. Historical counts must not be read as current test-discovery totals.

## Resource operations

`Schemata.Resource.Http` and `Schemata.Resource.Grpc` retain their package identities. `MapHttp()` and `MapGrpc()` activate their respective features on the resource builder. Both transports reach the same resource handlers and mutation owners.

| Operation | Shared owner | HTTP registration and presence | gRPC registration and presence | Classification | Executed evidence | Limits and parity boundary |
| --- | --- | --- | --- | --- | --- | --- |
| Create | `DefaultCreateResourceHandler` and `IResourceMutation<TEntity>` | `MapHttp()`; `POST` collection | `MapGrpc()`; `IResourceService.CreateAsync` | Dual transport | `ResourceHttpIntegrationShould.Post_NewStudent_Returns201` and `ResourceGrpcIntegrationShould.Create_ThenGet_ReturnsEntity`: 101 HTTP and 46 gRPC tests per TFM passed | The host supplies `Name` through an add advisor. Both paths commit through the same mutation owner. |
| Get | `DefaultGetResourceHandler` | `MapHttp()`; `GET` canonical name | `MapGrpc()`; `GetAsync` | Dual transport | HTTP and gRPC integration suites passed 101 and 46 tests per TFM | JSON detail fields and protobuf fields use different encodings; canonical identity and not-found meaning agree. |
| List with filter and pagination | `DefaultListResourceHandler` | `MapHttp()`; `GET` collection | `MapGrpc()`; `ListAsync` | Dual transport | `Get_PagedStudents_PreservesTotalPresenceAndContinuation` on both transports passed | Collection envelope names and total-size presence follow each wire contract; filter, continuation, and fresh read behavior are shared. |
| Update | `DefaultUpdateResourceHandler` and `IResourceMutation<TEntity>` | `MapHttp()`; `PATCH` canonical name | `MapGrpc()`; `UpdateAsync` | Dual transport | HTTP/gRPC integration and `ResourceGrpcErrorIntegrationShould.Update_WrongETag_ThrowsAborted` passed | Field masks use transport-specific binding. Stale freshness tags abort without a committed mutation. |
| Delete | `DefaultDeleteResourceHandler` and `IResourceMutation<TEntity>` | `MapHttp()`; `DELETE` canonical name | `MapGrpc()`; `DeleteAsync` | Dual transport | Delete and allow-missing cases in both integration suites passed | `allow_missing` has the same meaning. Empty protobuf responses and HTTP 204 carry no entity state. |
| Undelete, expunge, purge | `UndeleteHandler`, `ExpungeHandler`, `PurgeHandler`/`PurgeJob`, and the registered mutation pipeline | `MapHttp()`; custom method routes | `MapGrpc()`; corresponding custom RPCs | Dual transport | `PurgeExecutionShould`, `ResourceDryRunShould`, and continuation-binding cases in the listed suites passed | Lifecycle availability depends on entity traits and registration. These are not SCIM deletes. |
| Custom resource methods | `ResourceMethodDispatchHandler` | `MapHttp()`; verb-selected HTTP routes | `MapGrpc()`; unary custom RPCs | Dual transport | `CustomMethodGrpcShould`, HTTP built-in/custom-method, and operation-identity cases passed | HTTP verb selection and protobuf method shape differ. Authorization, handler selection, mutation ownership, and error classification stay shared. |
| Authorization, freshness, idempotency, errors | Dispatcher wraps and resource advisors | `MapHttp()` plus HTTP transport features | `MapGrpc()` plus gRPC transport features | Dual transport | HTTP/gRPC idempotency, continuation-binding, freshness, and error suites passed 101 and 46 tests per TFM | HTTP uses AIP JSON errors and gRPC uses status details. No-Write, validation, freshness, and dispatch failures retain their shared error meaning. |

## Security and provisioning protocols

Managers own native User/Role and Application/Scope/Token administration in process. OAuth/OIDC,
DCR, SCIM, redirects, and cookies are HTTP protocol surfaces; their HTTP-only classification is
intentional rather than a missing gRPC implementation. Use SCIM for external provisioning.
Resource.Http/Grpc remain available for application-defined resources.

| Operation | Shared owner | HTTP registration and presence | gRPC registration and presence | Classification | Executed evidence | Limits and parity boundary |
| --- | --- | --- | --- | --- | --- | --- |
| OAuth authorization code, refresh, client credentials, token exchange, device, revocation, introspection | Authorization endpoint handlers and the application/token stores | `UseAuthorization()` plus each flow's `Use…Flow()` or feature registration | No OAuth gRPC service | HTTP | `ScimProviderLifecycleShould.Dcr_Manager_And_Scim_Share_One_OAuth_Client` passed both providers and TFMs for the client-credentials leg | Other legs have existing `Schemata.Authorization.Integration.Tests` coverage and were not rerun in this task. Password grant remains a historical HTTP capability, not an RFC 9700 BCP-compliant profile. |
| OIDC discovery and JWKS | `DiscoveryHandler` and `JwksHandler` | `UseAuthorization()` and `UseWellKnown()` | No OIDC gRPC service | HTTP | Existing authorization discovery suites | Not rerun in this task. |
| DCR create/read | `RegisterHandler<TApp,…>` and `RegistrationMetadataMapper` | `UseDynamicClientRegistration()` | No DCR gRPC service | HTTP | The DCR/manager/SCIM theory passed EF Core and LinqToDB on net8 and net10 | Initial-access and registration-access tokens remain protocol credentials and are redacted from SCIM. |
| DCR replace/delete | `RegisterHandler<TApp,…>` | `UseRegistrationReplace()` and `UseRegistrationDelete()` | No DCR gRPC service | HTTP | Existing `RegisterReplaceFlowShould` and `RegisterRotationDeleteFlowShould` coverage | These DCR methods were not independently executed in this task. SCIM deletion and subsequent DCR read rejection were executed. |
| SCIM discovery | `ScimSchemaRegistry` and `IScimResourceHandler` | `UseScim()` on Identity or Authorization; discovery below `/scim/v2` | No SCIM gRPC service | HTTP | Both SCIM suites passed; Newman executed discovery twice per provider | Only registered handlers appear. Application schema URIs containing `/` are read through the Schemas collection in the lifecycle test. |
| SCIM User lifecycle | `ScimUserHandler<TUser>` and `SchemataUserManager<TUser>` | `UseIdentity<TUser,TRole>().UseScim()` | No SCIM gRPC service | HTTP | Identity suite: 41 tests per TFM; Newman: 17 requests per provider | Bulk, sort, password change, Enterprise User, ETag, cursor pagination, and security events are outside the supported profile. |
| SCIM Group lifecycle | `ScimGroupHandler<TUser,TRole>` and the user/role managers | `UseIdentity<TUser,TRole>().UseScim()` | No SCIM gRPC service | HTTP | Identity suite and external collection passed on both providers | Direct User members only. Nested Groups are rejected. |
| SCIM Application lifecycle | `ScimApplicationHandler<TApp>`, `IApplicationManager<TApp>`, and `RegistrationMetadataMapper` | `UseAuthorization(…).UseScim()` | No SCIM gRPC service | HTTP | Authorization suite: 17 tests per TFM; Newman: 17 requests per provider | SCIM exposes public metadata and delegated permissions, not registration credentials or raw token management. |
| Redirect and interaction | Authorization interaction and sign-in handlers | Authorization flow and interaction feature registration | No redirect gRPC service | HTTP | Existing authorization interaction suites | Redirect URI, cookie, form, and browser semantics are HTTP-only. |
| Cookie and browser session | Cookie authentication and session features | Authentication/Identity HTTP features | No cookie gRPC service | HTTP | Existing browser and session suites | SCIM provisioning issues no cookie or token. |
| User and Role managers | `SchemataUserManager<TUser>` and `SchemataRoleManager<TRole>` | No native management controller | No native management service | Runtime-only | Native manager/SCIM read and mutation assertions passed both providers and TFMs | Call the managers in process or the SCIM HTTP profile remotely. |
| Application, Scope, and Token stores/managers | Authorization managers and stores | No native management controller | No native management service | Runtime-only | Application manager, DCR, SCIM, and OAuth consumption assertions passed | Use the owning protocol for remote work. There is no parallel raw CRUD surface. |
| Security keys, access policy, and sessions | Security stores and policy services | No raw security management controller | `Schemata.Security.Grpc` contains gRPC signature-message helpers, not a management service | Runtime-only | Existing Security suites | No native security-management parity is claimed. |

## Linked domain surfaces

| Domain | Owner and registration | Classification | Evidence link | Task 15 boundary |
| --- | --- | --- | --- | --- |
| Push | `PushControlHandler`; `UsePush().MapHttp()` and the dedicated Push gRPC registration | Dual transport through Push-specific contracts | [Push control plane](push/control-plane.md), `PushControlShould`, and `PushControlGrpcShould` | Linked rather than duplicated. Push suites were not rerun in this task. |
| Messaging | `InProcessRequestDispatcher` and `IStreamDispatcher`; in-process, HTTP/gRPC stream adapters, or RabbitMQ registration | Runtime-only dispatcher plus separately registered adapters | [Messaging](messaging/overview.md), [Streams](messaging/streams.md), and Messaging test projects | Messaging has no SCIM or native security-management surface. Broker evidence was not rerun. |
| Quota | `QuotaControlHandler`; `UseQuotaAccounting().MapHttp().MapGrpc()` | Dual transport | [Quota transports](quota/transports.md), `QuotaControlShould`, and `QuotaControlGrpcShould` | Linked rather than duplicated. Quota suites were not rerun in this task. |

## Later-change parity gate

Every later native management change must name its shared-domain owner in the review. A change that adds or alters a remote operation must execute the corresponding HTTP and gRPC scenarios before claiming parity, including authorization, mutation commit, fresh-state readback, and error meaning. When only one transport exists, the review must record that classification and the reason instead of claiming dual-transport support.

A parity claim must update this matrix with executed evidence and limitations. Protocol additions must also update the relevant protocol matrix and transport registration documentation.
