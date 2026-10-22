# Open Gitea issues — cyp/Schemata (snapshot 2026-10-20)

Total: 58 open issues. Source: Gitea MCP `list_issues`/`issue_read` against http://raid.tekoki.internal:9000/cyp/Schemata (snapshot).

| # | Title | Labels | Updated |
|---|-------|--------|---------|
| 27 | Actor: implement Orleans cross-node single-owner execution | Area/Actor, Kind/Feature, Priority/High | 2026-09-21 |
| 36 | Authorization: implement CIBA core requests and shared poll lifecycle | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-17 |
| 38 | Authorization: encrypt ID tokens for their intended client | Area/Authorization, Kind/Feature, Priority/Medium | 2026-09-21 |
| 41 | Security: rotate active signing and encryption keys atomically across nodes | Area/Security, Kind/Feature, Priority/Medium | 2026-09-28 |
| 44 | Actor: reduce request-dispatch reflection cost without capturing scopes | Area/Actor, Kind/Enhancement, Priority/Low | 2026-09-21 |
| 49 | Rules: evaluate versioned decision tables through keyed expression compilers | Area/Expressions, Kind/Feature, Priority/Medium | 2026-09-28 |
| 53 | BFF: add versioned named frontend query views | Kind/Feature, Priority/Medium | 2026-09-21 |
| 59 | Quota: expose authorized reservation and ledger controls over HTTP and gRPC | Kind/Feature, Priority/Medium | 2026-09-21 |
| 61 | Quota: implement durable transactional usage reservations and accounting | Kind/Feature, Priority/Medium | 2026-10-03 |
| 100 | Authorization: retain an explicitly opted-in historical password grant | Area/Authorization, Kind/Feature, Priority/Medium | 2026-09-21 |
| 101 | Authorization: support signed-then-encrypted JAR Request Objects | Area/Authorization, Kind/Feature, Priority/Medium | 2026-09-21 |
| 102 | Authorization: resolve registered external request_uri through shared JAR validation | Area/Authorization, Kind/Feature, Priority/Medium | 2026-09-21 |
| 107 | Authorization: register complete implicit, hybrid and none response flows | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-19 |
| 108 | Authorization: deliver CIBA ping notifications through Scheduling | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-17 |
| 109 | Authorization: deliver stable CIBA push results with token hash bindings | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-17 |
| 110 | Authorization: publish profile-specific interoperability evidence and usable configuration | Area/Authorization, Kind/Testing, Priority/Medium | 2026-09-21 |
| 112 | Authorization: document and verify a working browser OAuth deployment profile | Area/Authorization, Kind/Documentation, Priority/Medium | 2026-09-21 |
| 114 | Authorization: validate the signed CIBA authentication-request profile | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-17 |
| 115 | Authorization: support opt-in resource step-up challenges and AS enforcement | Area/Authorization, Kind/Feature, Priority/Medium | 2026-09-21 |
| 118 | Authorization: derive CIBA pairwise sectors from mode-specific authority | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-17 |
| 123 | SCIM: implement one shared HTTP protocol with schema-aware queries and atomic PATCH | Area/Core, Kind/Feature, Priority/Medium | 2026-10-17 |
| 124 | Identity.Scim: provision Users through shared Identity admission and transactions | Area/Identity, Kind/Feature, Priority/Medium | 2026-10-17 |
| 125 | Identity.Scim: provision authorized role-backed Groups atomically | Area/Identity, Kind/Feature, Priority/Medium | 2026-10-17 |
| 126 | Authorization.Scim: provision Applications through authoritative metadata and protected policy | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-17 |
| 127 | SCIM: publish exercised provisioning interoperability and supported limits | Area/Identity, Kind/Testing, Priority/Medium | 2026-10-17 |
| 128 | Transport: publish operation-level management parity and protocol exceptions | Area/Core, Kind/Documentation, Priority/Medium | 2026-10-17 |
| 196 | Transport.Http: shared RFC 8288 Web Linking model and Link header serialization | Area/Core, Kind/Feature, Priority/Medium | 2026-10-17 |
| 197 | Resource: emit RFC 8288 links for pagination and resource relationships | Area/Resource, Kind/Feature, Priority/Medium | 2026-10-17 |
| 198 | Scheduling: expose RFC 8288 links between jobs, executions and owners | Kind/Feature, Priority/Medium | 2026-10-17 |
| 200 | Push: expose RFC 8288 links for subscriptions, providers and targets | Area/Push, Kind/Feature, Priority/Medium | 2026-10-17 |
| 201 | Flow: expose RFC 8288 links between definitions, instances, tokens and catches | Area/Flow, Kind/Feature, Priority/Medium | 2026-10-17 |
| 202 | Authorization: expose RFC 8288 links across Application, Scope and Token management resources | Area/Authorization, Kind/Feature, Priority/Medium | 2026-10-17 |
| 203 | Identity: expose RFC 8288 links across User, Role and session resources | Area/Identity, Kind/Feature, Priority/Medium | 2026-10-17 |
| 204 | Insight: expose RFC 8288 links for queries, sources and result pages | Area/Insight, Kind/Feature, Priority/Medium | 2026-10-17 |
| 205 | Report: expose RFC 8288 links across definitions, snapshots, chunks and schedules | Area/Report, Kind/Feature, Priority/Medium | 2026-10-17 |
| 285 | Flow.Bpmn: bind call-activity completion to the current child activation |  | 2026-10-17 |
| 291 | Comment-blind lookahead misclassifies annotated typed ViewFields |  | 2026-10-17 |
| 296 | Authorization-details parsing never disposes its JsonDocument |  | 2026-10-17 |
| 334 | Tenancy: cookie registration writes a debug probe.log on every options build |  | 2026-10-17 |
| 336 | Entity: repository/cache suppression scopes are state flags carried through AdviceContext |  | 2026-10-19 |
| 337 | Tenancy: provider cache serializes request threads and uses a ThreadStatic construction guard |  | 2026-10-17 |
| 338 | Tenancy: composition relies on fake tenant ids, concrete-type checks, descriptor removal and runtime probes |  | 2026-10-17 |
| 342 | Tenancy: SessionCache blocks synchronously on asynchronous cache operations |  | 2026-10-17 |
| 343 | Framework hygiene: stale package directories, unused skeleton reference, byte-identical constants and mapper overload surface |  | 2026-10-19 |
| 345 | Scheduling: finalization settles execution before a second dispatcher transaction stages job state |  | 2026-10-17 |
| 347 | Resource/Identity: duplicate handled-response carriers in explicit execution payloads |  | 2026-10-19 |
| 349 | Tenancy: auto-inject infrastructure isolation and remove application-specific per-entity wiring |  | 2026-10-17 |
| 350 | Actor/Flow/Scheduling: remove default ITenant entities and accept custom tenant-aware entity types |  | 2026-10-17 |
| 351 | Authorization: omitted response_mode bypasses AllowedResponseModes and error callbacks lose the effective mode |  | 2026-10-19 |
| 352 | Authorization: PKCE advisor applies to implicit and hybrid response types |  | 2026-10-19 |
| 353 | Resource/Security: instance PERMISSION_DENIED detail bypasses IPermissionResolver |  | 2026-10-19 |
| 354 | Actor: ActorSerializingHandler is duplicated in Flow, Push and Report |  | 2026-10-19 |
| 355 | Report: mutable ReportExecutionContext carries per-run state through a scoped service |  | 2026-10-19 |
| 356 | Resource: parent canonical derivation is duplicated with different validation |  | 2026-10-19 |
| 357 | Authorization: InteractionTokenFormat option is never read |  | 2026-10-19 |
| 358 | Modular/Core: SchemataOptionsExtensions has the same full name in two assemblies |  | 2026-10-19 |
| 359 | Entity: [ResourceReference] validation is silently disabled without IResourceTypeResolver |  | 2026-10-19 |
| 360 | Resource: Create/Update commit path depends on idempotency execution |  | 2026-10-19 |

## Bodies
### #27 — Actor: implement Orleans cross-node single-owner execution

## Goal (X)

Serialize a logical actor's business turns across nodes with real ownership/recovery rather than process-local exclusion.

## X–Y analysis

A local dictionary or optimistic state token does not confer cluster ownership, and a lock-only substitute cannot satisfy the requested Orleans provider. Goal is sound; keep independent from local lifecycle/reference work.

## Canonical responsibility

Schemata.Actor.Orleans provider behind existing Actor entrypoints, using tenant-bound grain identity and strong-consistency directory ownership.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior 9f2fac9 inventory finds no Orleans source/package/dependency; InProcessActorSystem owns only a local ConcurrentDictionary and default Actor registration selects that provider.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No real-silo or partition validation; provider version/configuration must be substantiated during implementation, not inferred here.

## Acceptance criteria

- [ ] Use a real supported Orleans provider/directory configuration with stable wire type registration; no fallback to the local provider.
- [ ] Across two real silos/client nodes, one logical identity has at most one active business turn; different tenants/identities progress independently.
- [ ] Non-reentrant lifecycle, state recovery, owner death, partition and stale-reference outcomes prevent conflicting state commits.
- [ ] Do not claim exactly-once arbitrary external effects; preserve domain transaction/idempotency responsibilities.

## Dependencies and related deliverables

#26, #20, #23, #69

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Implement Schemata.Actor.Orleans through existing Actor entrypoints using tenant-bound grain identity and a strong-consistency directory version. Preserve non-reentrant lifecycle, state recovery and stable wire type registration; no fallback to local provider or lock-only substitute.

## Current evidence

Two independent actor systems execute the same ActorId concurrently; documentation and optimistic concurrency do not supply cluster ownership.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 5.1–5.3. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Actor.Skeleton/ActorId.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Actor.Skeleton/ActorId.cs)
- [src/Schemata.Actor.Foundation/Runtime/InProcessActorSystem.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Actor.Foundation/Runtime/InProcessActorSystem.cs)
- [src/Schemata.Actor.Foundation/Runtime/ActorInstance.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Actor.Foundation/Runtime/ActorInstance.cs)

## Acceptance criteria

- [ ] Two real silos/client nodes: same logical identity maximum active business turns is one.
- [ ] Different tenants/identities progress independently.
- [ ] Owner death, partition, recovery and stale reference scenarios have explicit outcomes and no conflicting state commits.
- [ ] Do not claim exactly-once arbitrary external side effects; preserve domain transaction and idempotency responsibilities.

## Dependencies

- #26 — Actor: use logical references and serialize activation retirement
- #20 — Messaging: unify receiver execution scopes with explicit Enter lifecycle

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:orleans -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #36 — Authorization: implement CIBA core requests and shared poll lifecycle

## Goal (X)

Allow a client to initiate genuine out-of-band user authentication and redeem its authorized result once through CIBA poll mode.

## X–Y analysis

Goal is sound; keep independent because the shared request/state/poll lifecycle is a deliverable, while ping, push, signed requests and pairwise sectors are separate extensions. Backchannel logout, Native SSO or a renamed device flow is not CIBA.

## Canonical responsibility

Opt-in CIBA endpoint/grant registration and persisted client-bound request lifecycle, using consumer interaction and the existing shared issuer.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Fresh #36 body and discussion explicitly decompose these slices. Prior 9f2fac9e source inventory finds no CIBA handler, model, feature or auth_req_id lifecycle; existing TokenService does not implement this state machine.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Static absence inventory reused; no CIBA flow executed or claimed supported.

## Acceptance criteria

- [ ] Registered CIBA requests authenticate the client, require openid and exactly one supported login hint, validate applicable notification-token syntax/length, and expose coherent client/OP metadata.
- [ ] Accepted requests return HTTP 200 with an opaque unguessable auth_req_id, expires_in and applicable interval.
- [ ] A consumer-provided real identification/authentication/authorization decision precedes issuance; missing interaction fails at operation use, not startup.
- [ ] Pending, slow_down, denial, expiry, wrong-client, replay and concurrent completion outcomes follow the selected CIBA profile.
- [ ] Poll through urn:openid:params:grant-type:ciba issues the shared issuer's access/ID and applicable refresh tokens once.
- [ ] One registration/state contract supports #108/#109/#114/#118; completing this issue does not declare those modes complete, and mTLS remains excluded.

## Dependencies and related deliverables

#22, #35, #32, #76, #131, #108, #109, #114, #118, #131

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Implement the CIBA backchannel authentication request and shared persisted lifecycle with a complete poll-mode token flow. The existing complete-CIBA scope is decomposed into separately actionable delivery/protection changes linked below; none of the requested modes is dropped.

## Evidence and specification

At `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`, CIBA is absent, distinct from back-channel logout and Native SSO. Sources: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md` §6.5 and `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md` Stage H. Local documents are not assumed published remotely.

[OpenID Connect CIBA Core 1.0](https://openid.net/specs/openid-client-initiated-backchannel-authentication-core-1_0.html) §§4,7–8,10.1,11.

## Change

Register the mode through Use* DI composition and reuse the existing keyed grant, client authentication, token store and issuer. Add standard client/OP metadata, a backchannel endpoint accepting exactly one of login_hint/login_hint_token/id_token_hint and openid scope, consumer-owned user identification/authentication/authorization, and persisted auth_req_id state. Validate notification-token syntax/length where applicable. Return200 with auth_req_id/expires_in and applicable interval. Poll uses urn:openid:params:grant-type:ciba, client binding and one-time redemption. Missing user-interaction implementation fails at operation use, not startup. mTLS remains excluded from this issue.

## Acceptance criteria

- [ ] Authenticated requests return an opaque unguessable auth_req_id and proper metadata.
- [ ] Exactly-one hint and required parameter checks return protocol errors.
- [ ] Pending, slow_down, denial, expiry, wrong-client use, replay and concurrent completion follow the specification.
- [ ] Successful poll returns the shared issuer's access/ID tokens and applicable refresh token once.
- [ ] Explicit user decision is obtained through the consumer implementation before issuance.
- [ ] The registration/state contracts support the separately tracked ping/push/signed/PPID extensions without parallel protocol implementations.

## CIBA completion slices

- #108 — ping callbacks.
- #109 — push results and hash bindings.
- #114 — signed authentication requests.
- #118 — mode-specific pairwise sectors and key proof.

## Dependencies

- #22 — real atomic cache capabilities.
- #35 — one effective authorization options source.
- #32 — one optional session service.
- #76 — standard client metadata model.
- #70 — client authentication input propagation.

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Build changed code once and exercise focused CIBA behavior on an isolated target. No existing test count establishes CIBA support. Callback retries must not reissue tokens; completion of this poll/core issue alone is not completion of all CIBA modes.

<!-- schemata-component-spec:ciba -->
<!-- schemata-authorization-audit:existing-ciba-core -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #38 — Authorization: encrypt ID tokens for their intended client

## Goal (X)

Protect ID-token confidentiality for clients that request encryption while retaining a verifiable signed inner token and existing profile bindings.

## X–Y analysis

Server access-token encryption keys and UserInfo encryption are not implementations of recipient-specific ID-token encryption. Goal is sound; keep independent from #89 signing negotiation because recipient key selection and nested encryption are an additional deliverable.

## Canonical responsibility

One shared ID-token issuance/protection path composes accepted signing from #89 with client-specific encryption metadata and eligible public client keys; all existing and CIBA issuance consumers reuse it.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

TokenService.CreateIdToken.cs:249-278 accepts signing preference but no recipient/encryption metadata and ends in CreateToken(claims,lifetime). Prior current source inventory finds no ID-token encrypted metadata consumers. Direct OIDC Registration Final §2 read for nested signing/encryption and metadata defaults; UserInfoResponseProtector is a distinct output path, not completion.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Feature remains unimplemented; no cryptographic or regression execution claimed.

## Acceptance criteria

- [ ] Register, persist and read id_token_encrypted_response_alg/enc; reject enc without alg and apply A128CBC-HS256 when alg is present and enc omitted.
- [ ] Issue signed-then-encrypted nested ID tokens decryptable only by the intended client, with a verifiable inner signature.
- [ ] Missing/incompatible client keys or unsupported algorithms fail without plaintext downgrade or substitution of server access-token encryption keys.
- [ ] Existing and CIBA issuance share the protector while preserving nonce, at_hash/c_hash/ds_hash and applicable CIBA push bindings.
- [ ] Preserve PAR/JAR/Native SSO regression scope and one authoritative public-key metadata source; private keys remain with their legitimate owner.

## Dependencies and related deliverables

#35, #76, #89, #109, #116

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Add id_token_encrypted_response_alg/enc registration and shared signed-then-encrypted protector using client keys. Apply it to existing and CIBA issuance; no server access-token-key substitution or plaintext fallback.

## Current evidence

CreateIdToken currently emits signed JWT without per-client JWE response metadata.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 6.6. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Authorization.Foundation/Services/TokenService.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/TokenService.cs)
- [src/Schemata.Security.Skeleton/Entities/SchemataSecurity.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Security.Skeleton/Entities/SchemataSecurity.cs)

## Acceptance criteria

- [ ] Only intended client decrypts and verifies the inner signature.
- [ ] enc without alg fails; default enc and algorithm validation follow registration contract.
- [ ] Missing/incompatible client key never downgrades to plaintext.
- [ ] PAR/JAR/Native SSO issuance regressions pass.

## Dependencies

- #35 — Authorization: use one options source for authentication schemes and policies

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:idtokenencryption -->

## Authorization protocol audit alignment

Source: `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, F07 / Stage D. Apply [OIDC Registration §2](https://openid.net/specs/openid-connect-registration-1_0.html) metadata `id_token_encrypted_response_alg` and `id_token_encrypted_response_enc`; when alg is specified and enc omitted, use the standard applicable default A128CBC-HS256. This default is protocol-specific and does not change the framework's access-token encryption default. Keep client-specific signing preference and encryption separate but composed in the same issuer; retain at_hash/c_hash/ds_hash and CIBA push bindings. Public jwks/jwks_uri metadata must have one authoritative source; private key material stays in Security.

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

<!-- schemata-authorization-audit:existing-idtoken-encryption -->
Additional dependencies: #76 (standard metadata) and #89 (client signing preference). CIBA push uses this shared protector in #109.
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #41 — Security: rotate active signing and encryption keys atomically across nodes

## Goal (X)

Rotate key material proactively without split active-key selection, lost decryptability or delayed revocation.

## X–Y analysis

Selecting the first currently valid row is not an atomic rotation protocol. Goal is sound; scheduling, key retention and external key-material integration are independent of token clock-skew or token transport fixes.

## Canonical responsibility

Security persistent active-key/version authority and rotation transaction; Security.Scheduling triggers the operation.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior 9f2fac9 trace: TokenService.cs:55-57 and86-98 select first valid signing/encryption rows; :65-67 accepts valid/retired verification keys. No Security.Scheduling rotation capability or atomic active-pointer transition found.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Source-gap assessment only; no key rotation, retention or concurrent-node execution.

## Acceptance criteria

- [ ] Persist active-key pointer/version; stage key publication before one atomic cross-node switch.
- [ ] Concurrent rotation attempts select one active transition and failure leaves the previous active key usable.
- [ ] Retired keys verify/decrypt existing artifacts for the required retention window; revoked keys fail immediately.
- [ ] Schedule proactive signing and encryption rotation while preserving external key-material integration.
- [ ] Consumer naming advisors remain responsible for resource Names.

## Dependencies and related deliverables

#37

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Add Security.Scheduling rotation with persistent active-key pointer/version, staged publication, atomic switch, retired retention and immediate revocation. Preserve external key-material integration.

## Current evidence

Current services select valid/retired key rows but do not own proactive rotation scheduling.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 6.6. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Authorization.Foundation/Services/TokenService.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/TokenService.cs)
- [src/Schemata.Security.Skeleton/Entities/SchemataSecurity.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Security.Skeleton/Entities/SchemataSecurity.cs)

## Acceptance criteria

- [ ] Concurrent nodes switch active pointer once.
- [ ] Rotation failure leaves prior key usable.
- [ ] Old artifacts verify/decrypt through required retention window; revoked keys fail.
- [ ] Resource Names come from consumer naming advisors.

## Dependencies

None beyond existing framework capabilities.

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:keyrotation -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #44 — Actor: reduce request-dispatch reflection cost without capturing scopes

## Goal (X)

Reduce measured actor dispatch overhead while preserving current-turn handler resolution and bounded metadata retention.

## X–Y analysis

Goal is sound; keep independent because lifecycle correctness does not reduce per-message interface scans or prove throughput/allocation improvement. Caching handler instances or scope-capturing delegates would trade cost for incorrect lifetimes.

## Canonical responsibility

RequestDispatchingActor request/response dispatch metadata lookup, not handler ownership.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

9f2fac9 fresh read: RequestDispatchingActor.cs:31-39 calls ResolveResponseType/MakeGenericMethod per receive; :56-65 scans interfaces; DispatchAsync resolves keyed handler from ctx.Services each time.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No benchmark or performance claim; original measurement acceptance remains open.

## Acceptance criteria

- [ ] Reuse bounded metadata-only cache entries or explicitly registered type metadata; mixed request/response types dispatch correctly.
- [ ] Eviction only recomputes metadata and cannot change dispatch semantics.
- [ ] Resolve the current scoped handler for every turn; retain no provider or scoped handler in shared metadata.
- [ ] Record comparable before/after allocations and throughput at the actual dispatch entry before claiming an improvement.

## Dependencies and related deliverables

#26, #132

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Use bounded metadata-only cache or registered type metadata; never cache scoped handlers/delegates capturing provider. Measure actual dispatch allocations/throughput before claiming improvement.

## Current evidence

RequestDispatchingActor resolves request/response interfaces and closes MethodInfo per message.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 7.5. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Flow.Foundation/ProcessPersistence.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Flow.Foundation/ProcessPersistence.cs)
- [src/Schemata.Actor.Foundation/Runtime/RequestDispatchingActor.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Actor.Foundation/Runtime/RequestDispatchingActor.cs)

## Acceptance criteria

- [ ] Mixed request types dispatch correctly and eviction only recomputes metadata.
- [ ] Scoped handler identity remains current per turn.
- [ ] Record before/after measurement at the actual dispatch entry.

## Dependencies

None beyond existing framework capabilities.

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:reflectioncache -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #49 — Rules: evaluate versioned decision tables through keyed expression compilers

## Goal (X)

Provide reusable typed decision-table evaluation independently of any workflow engine or expression language.

## X–Y analysis

Goal is sound; keep independent because expression compilation is not a table runtime and Flow integration48 is only one consumer. The requested extension contract is justified, but a second expression compiler/cache or implied DMN compatibility is not.

## Canonical responsibility

Rules contracts/runtime for immutable table versions and hit policy, delegating expression parsing/compilation to existing keyed Expressions services.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior9f2fac9 inventory has no Rules package/runtime. Existing src/Schemata.Expressions.Skeleton/IExpressionCompiler.cs exposes language/parse/compile, not decision-table hit policies; BusinessRuleTask lacks an evaluator.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Existing contract evidence reused; no Rules runtime or conformance execution exists to verify.

## Acceptance criteria

- [ ] A decision table has immutable versioned identity, typed inputs/outputs and explicitly defined Unique, First, Collect and NoMatch results.
- [ ] Expressions select Language individually; two keyed languages coexist in one rule set and a third compiler integrates without Rules or Flow changes.
- [ ] Unknown languages, type errors and zero/multiple matches follow defined observable outcomes, including Unique violations and deterministic First/Collect ordering.
- [ ] Reuse IExpressionCompiler and ExpressionCache semantics; do not claim FEEL/DMN compatibility or permit implicit arbitrary I/O through the table contract.

## Dependencies and related deliverables

#48, #67

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Add Rules contracts/runtime with typed input/output, immutable version, per-expression Language, Unique/First/Collect and explicit NoMatch. Reuse IExpressionCompiler/ExpressionCache semantics; future Feel plugs through same contract.

## Current evidence

BusinessRuleTask has no dedicated decision table runtime; Expressions already exposes keyed compiler contract.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 8.2; 8.3. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Expressions.Skeleton/IExpressionCompiler.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Expressions.Skeleton/IExpressionCompiler.cs)

## Acceptance criteria

- [ ] Two keyed languages coexist in one rule set.
- [ ] Third compiler works without changes to Rules or Flow.
- [ ] Hit policies, type errors, unknown languages and zero/multiple matches have specified results.
- [ ] No implicit DMN/FEEL compatibility or arbitrary I/O is claimed.

## Dependencies

None beyond existing framework capabilities.

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:rules -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #53 — BFF: add versioned named frontend query views

## Goal (X)

Offer stable server-owned frontend query contracts without exposing arbitrary federated query power.

## X–Y analysis

Goal is sound; keep independent because safer Insight fields52 and reusable Insight contracts51 do not provide named frontend views, typed parameter binding or frontend/version-bound paging. BFF is a constrained consumer, not another general query engine/proxy.

## Canonical responsibility

BFF-owned registered view/version/input/output contract and authorization, delegating execution to Insight contracts through HTTP.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Fresh issue body and prior9f2fac9 inventory show no Bff/named-view implementation; Insight accepts client-shaped QueryInsightRequest. Existing planning/materialization is not a frontend contract.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Backlog/source evidence only; no BFF endpoint, paging or cache scenarios executed.

## Acceptance criteria

- [ ] Register named versioned views with fixed sources, query template, output model and frontend authorization; unknown fields/parameters/source overrides fail before execution.
- [ ] Bind typed parameters as values, never by concatenating expression text; reject arbitrary URL, SQL and source proxying.
- [ ] Paging tokens and cache identity bind to tenant, frontend, view/version and relevant query inputs; cross-boundary token reuse is rejected.
- [ ] Aggregated view execution returns all-success or an explicit failure, never an unmarked partial result, while preserving Insight field and row policy.

## Dependencies and related deliverables

#52, #51, #18, #67

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Implement BFF definitions and HTTP query through Insight contracts: fixed sources/template/output model, typed parameters, frontend authorization, bound paging/cache identity. No arbitrary URL/SQL/source proxy.

## Current evidence

Insight offers client-shaped queries but no fixed server-owned per-frontend view contract.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 9.2. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Insight.Foundation/Planning/InsightPlanBuilder.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Insight.Foundation/Planning/InsightPlanBuilder.cs)

## Acceptance criteria

- [ ] Unknown fields/parameters/source overrides fail before execution.
- [ ] Parameters bind as values, never concatenated expression text.
- [ ] Cross-tenant/frontend/version paging tokens are rejected.
- [ ] Aggregates are all-success or explicit failure, never unmarked partial result.

## Dependencies

- #52 — Insight: restrict query operations to an explicitly registered public model
- #51 — Report: depend on Insight planning/execution contracts instead of concrete implementations
- #18 — Tenancy: introduce immutable execution identity with scoped restoration

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:bff -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #59 — Quota: expose authorized reservation and ledger controls over HTTP and gRPC

## Goal (X)

Let remote callers safely reserve, commit, release and inspect durable usage accounting through the same domain rules as local callers.

## X–Y analysis

Goal is sound; keep independent because61 supplies accounting semantics while this issue supplies authenticated remote access and wire parity. ASP.NET rate limiting is neither a durable ledger nor a cross-service reservation transaction.

## Canonical responsibility

Quota HTTP/gRPC adapters over canonical accounting handlers; trusted execution identity and subject/service authorization remain shared domain policy.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior9f2fac9 inventory identifies only Core/Features/SchemataQuotaFeature.cs, which installs ASP.NET rate limiting, not durable reservation handlers or management transports. XYPlatform confirms61 remains its independent accounting owner.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No accounting transport execution; dependencies and all remote-control acceptance remain open.

## Acceptance criteria

- [ ] Expose the intended reserve/commit/release and ledger controls over both transports using the same accounting handlers and equivalent state/error meanings.
- [ ] Derive tenant from the trusted execution frame; reject caller-supplied tenant override and unauthorized manipulation of another subject's balance.
- [ ] Repeated operation IDs preserve accounting idempotency across transport retries and mixed HTTP/gRPC calls.
- [ ] Preserve explicit same-unit-of-work enlistment for local operations and the defined cross-service reservation protocol; do not imply remote requests share the caller's database transaction.

## Dependencies and related deliverables

#61, #34, #18, #128

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Add HTTP/gRPC endpoints over canonical Quota handlers, deriving tenant from trusted frame and enforcing subject/service permissions. Preserve same-UoW explicit enlistment and cross-service reservation protocol.

## Current evidence

Persistent accounting target needs authorized reserve/commit/release interfaces.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 10.3. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Core/Features/SchemataQuotaFeature.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Core/Features/SchemataQuotaFeature.cs)

## Acceptance criteria

- [ ] Both transports return equivalent state/errors.
- [ ] Caller cannot override tenant or modify another subject balance.
- [ ] Repeated operation IDs retain accounting idempotency.

## Dependencies

- #61 — Quota: implement transactional usage reservations and accounting
- #34 — Resource: enforce authentication for scheme-only resource configuration

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:quotatransport -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #61 — Quota: implement durable transactional usage reservations and accounting

## Goal (X)

Enforce usage limits across nodes with durable, idempotent reservation and settlement accounting.

## X–Y analysis

Rate limiting and cache CAS do not implement durable usage accounting. Goal is sound; keep independent from rejection metadata, quota transports and cache capability work.

## Canonical responsibility

Quota account/reservation/ledger persistence transaction; Scheduling only drives expiry.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior 9f2fac9 inventory finds no Quota accounting package/model; SchemataQuotaFeature adapts ASP.NET rate limiting only. Full issue preserves durable ledger and multi-metric UoW obligations. XYCapabilities retains #59 transport separately.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Feature-gap source assessment only; no concurrent-node/accounting execution.

## Acceptance criteria

- [ ] Accounts are scoped by tenant, subject, metric, window and policy version; atomic reserve enforces committed+reserved+requested <= limit across nodes.
- [ ] Repeated request_id does not double reserve or debit.
- [ ] Commit, release and expiry settle each reservation exclusively, including concurrent races.
- [ ] Multi-metric operations commit or roll back atomically; durable database state, not cache CAS, is authoritative.
- [ ] Scheduled expiry recovers pending durable reservations without double settlement.

## Dependencies and related deliverables

#18, #59, #60, #22

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Scope

Create tenant/subject/metric/window/policy-version accounts, reservations and ledger. Atomic reserve respects committed+reserved+requested<=limit; idempotent request_id; commit/release/expire exclusive. Database authority, Scheduling expiry and multi-metric UoW.

## Current evidence

Core Quota adapts rate limiting; it has no persistent usage balance/reservation model.

Source reviewed at `87ba3e4eba7ae5ecb15da58efb215c9921d85b78` (tree identical to the spec evidence baseline). Spec: `docs/superpowers/specs/2026-09-14-component-correctness-and-capabilities.md`, sections 10.3. The local spec is not assumed published remotely; the executable scope and acceptance criteria are reproduced here.

## Source locations

- [src/Schemata.Core/Features/SchemataQuotaFeature.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Core/Features/SchemataQuotaFeature.cs)

## Acceptance criteria

- [ ] Concurrent nodes cannot reserve beyond limit.
- [ ] Same request_id does not double debit.
- [ ] Commit/Release/Expire races resolve once and roll back atomically across metrics.
- [ ] No cache CAS substitutes durable accounting.

## Dependencies

- #18 — Tenancy: introduce immutable execution identity with scoped restoration

## Constraints and verification

This is a planned change, not an implementation claim. Keep one implementation and migrate callers without compatibility shims. Do not add startup validation/scans. Preserve consumer-owned resource Name generation and SnakeCaseLower dictionary keys. OAuth/OIDC mTLS is excluded. PAR, JAR and Native SSO already exist; shared authorization changes must preserve them.

Use focused behavioral tests for this change, including the edge cases above. Build changed code once before --no-build test runs; never claim unexecuted scenarios passed. No production implementation is performed by this issue synchronization.

<!-- schemata-component-spec:quotaaccounting -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #100 — Authorization: retain an explicitly opted-in historical password grant

## Goal (X)

Provide the user's explicitly requested historical password-grant capability without enabling it by default or bypassing real credential validation.

## X–Y analysis

Goal is an intentional historical compatibility/product choice, not a missing modern-security feature. Do not invalidate it merely because modern BCP prohibits its use; preserve opt-in and clearly label the nonconformant Relax posture and credential-exposure risk.

## Canonical responsibility

Explicit historical grant feature/keyed handler composed with existing client authentication, consumer credential validation and issuer.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

9f2fac9e flow inventory has no password feature/handler and TokenRequest lacks username/password; TokenHandler rejects unavailable keyed grants. Fresh RFC9700 primary text https://www.rfc-editor.org/rfc/rfc9700.txt §2.4 says the grant MUST NOT be used because it exposes resource-owner credentials and cannot support multi-step authentication. The fresh issue explicitly preserves the user's historical-mode decision.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No implementation or credential-flow execution. Standards warning is not a close-invalid recommendation.

## Acceptance criteria

- [ ] Without explicit feature registration, the grant is unavailable.
- [ ] An explicitly permitted historical profile validates real client/user credentials and issues only tokens allowed by standard client metadata and the selected policy.
- [ ] Modern enforcement rejects the grant; a documented explicit Relax posture is not represented as RFC9700 conformant and never permits invalid credentials.
- [ ] Credentials are not logged, persisted as resource Name or substituted for a consumer-owned identity mechanism.

## Dependencies and related deliverables

#131, #131

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

The user requires historical modes retained as optional capabilities; password grant is not currently implemented. Modern BCP forbids its use.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage E. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://www.rfc-editor.org/rfc/rfc6749.txt §4.3; https://www.rfc-editor.org/rfc/rfc9700.txt §2.4

## Source locations

- [src/Schemata.Authorization.Foundation/Features](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Features)
- [src/Schemata.Authorization.Foundation/Handlers](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Handlers)
- [src/Schemata.Authorization.Foundation/Extensions/SchemataAuthorizationBuilderExtensions.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Extensions/SchemataAuthorizationBuilderExtensions.cs)
- [src/Schemata.Authorization.Skeleton/Models/TokenRequest.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Skeleton/Models/TokenRequest.cs)

## Change

Implement one opt-in historical grant using existing client auth/token issuance and consumer credential validation. Modern enforcement normally blocks the incompatible mode; an explicit Relax posture must be documented as not RFC9700-conformant. Never bypass credential validation.

## Acceptance criteria

- [ ] Absent opt-in leaves the grant unavailable.
- [ ] Explicitly permitted historical profile validates client/user credentials and issues only permitted tokens.
- [ ] Modern enforcement rejects the grant; disabling policy does not accept invalid credentials.
- [ ] Credentials are neither stored as resource Name nor logged.

## Dependencies

- #70

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:password-grant -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #101 — Authorization: support signed-then-encrypted JAR Request Objects

## Goal (X)

Process negotiated encrypted Request Objects without weakening the existing signed request validation contract.

## X–Y analysis

Goal is sound; existing access-token JWE support does not provide Request Object decryption, client algorithm negotiation or discovery. Keep independent of #99 and #88.

## Canonical responsibility

Opt-in JAR envelope decryption/key resolution feeding the existing signed RequestObjectReader validation path.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

RequestObjectReader rejects dot counts other than two before validation; five-segment JWE cannot reach decryption. SchemataApplication exposes signing preference but no complete Request Object encryption negotiation.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Static feature/reader inventory; no encrypted JAR executed.

## Acceptance criteria

- [ ] Valid signed-then-encrypted Request Objects using negotiated algorithms are decrypted and processed through the existing authorization validation pipeline.
- [ ] Bad authentication tags, unsupported encryption algorithms and invalid inner signatures fail.
- [ ] Client and discovery encryption metadata describe the working algorithm/key behavior.
- [ ] Reuse Security key material and avoid a second authorization validator or unconditional encryption capability.

## Dependencies and related deliverables

#76, #99

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

RequestObjectReader rejects five-segment JWE requests; no decryption path or full encryption metadata exists.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage G. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://www.rfc-editor.org/rfc/rfc9101.txt §§4,6.1

## Source locations

- [src/Schemata.Authorization.Foundation/Services/RequestObjectReader.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/RequestObjectReader.cs)
- [src/Schemata.Authorization.Foundation/Features/JwtSecuredAuthorizationRequestsFeature.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Features/JwtSecuredAuthorizationRequestsFeature.cs)
- [src/Schemata.Authorization.Skeleton/Entities/SchemataApplication.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Skeleton/Entities/SchemataApplication.cs)
- [src/Schemata.Authorization.Skeleton/Models/DiscoveryDocument.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Skeleton/Models/DiscoveryDocument.cs)

## Change

Add opt-in JWE resolution before the existing signed request validation, with negotiated algorithms and correct metadata. Reuse Security key material; do not make a parallel authorization validator.

## Acceptance criteria

- [ ] Valid nested signed/encrypted objects are processed.
- [ ] Bad authentication tag, unsupported algorithms and invalid inner signatures fail.
- [ ] Published encryption metadata matches working algorithms.

## Dependencies

- #76

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:jar-encryption -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #102 — Authorization: resolve registered external request_uri through shared JAR validation

## Goal (X)

Allow selected external Request Object references while enforcing registration/network policy and preserving local PAR references.

## X–Y analysis

Goal is sound; supporting opaque local PAR references is not equivalent to fetching a registered external object. Retrieval/cache policy and final JAR validation are different responsibilities.

## Canonical responsibility

Opt-in external URI resolution with bounded HTTP policy, feeding one authoritative Request Object normalization/validation path.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

AdviceAuthorizeRequestUri rejects every non-PAR-prefix URI with request_uri_not_supported. SchemataApplication has no request_uris metadata and RequestObjectReader has no external resolver.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No network retrieval or cache scenario executed; no new remote-fetch implementation proposed beyond the original scope.

## Acceptance criteria

- [ ] A registered external URI is fetched under TLS and bounded retrieval policy and its signed Request Object is processed.
- [ ] Untrusted/disallowed URIs and malformed content fail with protocol errors rather than bypassing signature or registration checks.
- [ ] Cache invalidation and changed URI fragments follow a declared consistent contract.
- [ ] Local PAR request_uri resolution/redemption remains intact and both forms use the same final authorization validation.

## Dependencies and related deliverables

#76, #101, #135

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

Only locally issued PAR URIs are supported; external Request Object retrieval and request_uris registration are absent.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage G. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://www.rfc-editor.org/rfc/rfc9101.txt §§5.2,8,10.4; https://openid.net/specs/openid-connect-registration-1_0.html §2

## Source locations

- [src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeRequestUri.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeRequestUri.cs)
- [src/Schemata.Authorization.Foundation/Services/RequestObjectReader.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/RequestObjectReader.cs)
- [src/Schemata.Authorization.Skeleton/Entities/SchemataApplication.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Skeleton/Entities/SchemataApplication.cs)

## Change

Use an opt-in URI resolver with bounded HTTP retrieval, TLS verification, registration/network policy and cache semantics. Both external and local representations feed the same final request validation.

## Acceptance criteria

- [ ] Registered external URI is fetched and its signed object processed.
- [ ] Untrusted/disallowed URI and malformed content fail with protocol errors.
- [ ] Cache invalidation and changed URI fragments obey declared behavior.
- [ ] PAR local URI handling remains unchanged.

## Dependencies

- #76

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:jar-uri -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #107 — Authorization: register complete implicit, hybrid and none response flows

## Goal (X)

Make explicitly enabled historical browser flows complete and truthful across registration, dispatch, issuance and discovery.

## X–Y analysis

Callback rendering and per-client checks alone do not install a complete flow. The title's response modes should not conflate response_type combinations with encoding modes such as fragment/form_post. #72 and #135 remain dependencies, not replacements.

## Canonical responsibility

Explicit flow Use* registration composing the existing authorize pipeline, issuer, client metadata and discovery contributions.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

At 9f2fac9e AuthorizationCodeFlowFeature is the only browser flow registration; mapper accepts only code and AdviceAuthorizeAutoApproveSignIn.cs:109 stamps authorization_code. New standard metadata checks are partial progress, not full flow installation.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Static registration and issuer trace; no historical browser flow executed.

## Acceptance criteria

- [ ] Each explicitly enabled response combination has coherent ResponseTypes/GrantTypes metadata, actual registration acceptance and per-client authorization.
- [ ] response_type=none is exclusive and returns no code, access token or ID token.
- [ ] Pure implicit retains its real grant identity rather than being stamped authorization_code.
- [ ] Unregistered flows do not enter their flow-specific handlers/advisors or advertise availability.
- [ ] Historical opt-in and modern enforcement/Relax posture remain explicit without a second policy engine; legal success/error modes use #135.

## Dependencies and related deliverables

#72, #89, #97, #135

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

Callback assembly exists, but complete independent flow registration, grant correspondence, metadata and application permission behavior do not.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage E. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://openid.net/specs/oauth-v2-multiple-response-types-1_0.html §§3–5; https://openid.net/specs/openid-connect-core-1_0.html §§3.2–3.3

## Source locations

- [src/Schemata.Authorization.Foundation/Features](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Features)
- [src/Schemata.Authorization.Foundation/Extensions/SchemataAuthorizationBuilderExtensions.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Extensions/SchemataAuthorizationBuilderExtensions.cs)
- [src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeGrantPermission.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeGrantPermission.cs)
- [src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs)
- [src/Schemata.Authorization.Foundation/Services/RegistrationMetadataMapper.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/RegistrationMetadataMapper.cs)

## Change

Provide explicit Use* opt-in registrations for supported historical response modes, reusing the authorization pipeline/renderer/issuer. Keep default enforcement and Relax behavior DI-first, without a second policy engine.

## Acceptance criteria

- [ ] Every enabled combination has coherent grant/response metadata and per-client authorization.
- [ ] none returns no code/access/ID token and is not improperly combined.
- [ ] Pure implicit is not disguised as code grant.
- [ ] Unregistered flows do not enter their handlers/advisors.

## Dependencies

- #72
- #89

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:historical-responses -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #108 — Authorization: deliver CIBA ping notifications through Scheduling

## Goal (X)

Notify a ping-mode client when its CIBA request has an outcome while preserving one-time token retrieval.

## X–Y analysis

Goal is sound; ping is a notification plus later retrieval contract, not duplicate poll-core work and not a token push or logout delivery.

## Canonical responsibility

CIBA state transition to a Scheduling notification job, followed by the shared poll redemption owner.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Fresh #108 requirements and 9f2fac9e complete source inventory show no CIBA ping feature/service. Existing logout delivery and ordinary issuer do not implement ping semantics.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No callback or polling execution; retain separately from the scoped core parent.

## Acceptance criteria

- [ ] Success and failure outcomes schedule the correct ping notification from the shared CIBA state.
- [ ] The JSON body contains only auth_req_id and uses the registered HTTPS endpoint and client_notification_token Bearer credential.
- [ ] 3xx redirects are not followed and delivery failures remain observable to Scheduling.
- [ ] The client may poll before notification and can retrieve the authorized tokens only once; notification retry does not reissue them.

## Dependencies and related deliverables

#36, #109

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

CIBA ping delivery has no implementation. The existing CIBA issue is retained for the shared request/poll lifecycle.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage H. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://openid.net/specs/openid-client-initiated-backchannel-authentication-core-1_0.html §§9,10.2

## Source locations

- [src/Schemata.Authorization.Foundation/Features](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Features)
- [src/Schemata.Authorization.Foundation/Services](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services)
- [src/Schemata.Authorization.Skeleton/Entities/SchemataApplication.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Skeleton/Entities/SchemataApplication.cs)

## Change

Add the registered ping delivery mode using Scheduling and the existing CIBA state. Send JSON auth_req_id with the client_notification_token Bearer credential to the registered HTTPS endpoint; never follow redirects. Ping clients may also poll.

## Acceptance criteria

- [ ] Successful/failed authentication produces the correct ping notification.
- [ ] Only auth_req_id is in the ping body; registered credential/endpoint are used.
- [ ] 3xx is not followed; delivery failures remain observable.
- [ ] Ping client can retrieve tokens once and may poll before receiving notification.

## Dependencies

- #36

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:ciba-ping -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #109 — Authorization: deliver stable CIBA push results with token hash bindings

## Goal (X)

Deliver an already-authorized CIBA result directly and safely to a registered push client without duplicate issuance on retries.

## X–Y analysis

Goal is sound; push has distinct result/hash/poll-prohibition semantics that cannot be absorbed by poll core or ping delivery merely because they share Scheduling.

## Canonical responsibility

CIBA terminal result issuance/persistence and Scheduling delivery through the shared issuer and JOSE facilities.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

No CIBA production feature/model/handler exists in prior 9f2fac9e inventory. AuthorizationSignInService.cs:183-303 issues ordinary token/callback results, not persisted push delivery.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No push, hash-binding or retry scenario executed; retain independent completion slice.

## Acceptance criteria

- [ ] Success includes auth_req_id and an ID token with at_hash and urn:openid:params:jwt:claim:auth_req_id, plus urn:openid:params:jwt:claim:rt_hash when a refresh token is delivered.
- [ ] Denial/error delivery includes error and auth_req_id.
- [ ] Push-client token polling returns unauthorized_client.
- [ ] Notifications use the registered HTTPS endpoint/Bearer credential and never follow redirects.
- [ ] Retries deliver the persisted result without reissuing tokens or changing the bound payload; delivery failures remain visible to Scheduling.

## Dependencies and related deliverables

#36, #89, #38, #108

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

CIBA push mode and its bound token/error payload are not implemented.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage H. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://openid.net/specs/openid-client-initiated-backchannel-authentication-core-1_0.html §§10.3,11–12

## Source locations

- [src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs)
- [src/Schemata.Authorization.Foundation/Services](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services)
- [src/Schemata.Authorization.Skeleton/Models](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Skeleton/Models)

## Change

Deliver through Scheduling only in the client's registered push mode; include auth_req_id and required token hashes. Forbid CIBA polling by push clients; never follow notification redirects or reissue tokens on delivery retry.

## Acceptance criteria

- [ ] Success contains at_hash and urn:openid:params:jwt:claim:auth_req_id in ID Token, plus urn:openid:params:jwt:claim:rt_hash when a refresh token is sent.
- [ ] Denial/error payload contains error and auth_req_id.
- [ ] Push client token polling returns unauthorized_client.
- [ ] Notification uses registered HTTPS endpoint/Bearer credential and rejects redirects.

## Dependencies

- #36
- #89
- #38

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:ciba-push -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #110 — Authorization: publish profile-specific interoperability evidence and usable configuration

## Goal (X)

Give consumers an honest, reproducible account of which configured OAuth/OIDC profiles actually interoperate and which obligations remain application-owned.

## X–Y analysis

A passing unit/component suite is not cross-profile browser/RP conformance. Keep this independent from112's concrete browser deployment recipe and128's management transport matrix; it aggregates evidence without replacing those deliverables.

## Canonical responsibility

Authorization profile requirement-to-wire evidence matrix, executable configuration documentation and external/browser interoperability records.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Fresh issue110 includes obsolete blanket e:/g:/r:/s: wording requiring reconciliation with9f2fac9. Prior evidence cites docs/documents/authorization.md:19-36 untyped UseIdentity examples and450-465 status table, not browser/RP transcripts.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No browser, external RP, certification or new test execution; historical counts are not current profile acceptance.

## Acceptance criteria

- [ ] For each enabled profile map requirements to actual wire scenarios, implementation owners and application responsibilities; distinguish source presence, exercised behavior and unverified requirements.
- [ ] Document configurations that resolve actual DI registrations and current per-application policy; standard GrantTypes/ResponseTypes/Scope metadata stays authoritative at9f2fac9 rather than restoring obsolete g:/r:/s: metadata authority.
- [ ] Record substantive browser iframe/form_post/CORS and external RP scenarios on compliant isolated targets, including relevant boundary/state/concurrency cases and exact limitations.
- [ ] Preserve opt-in historical configurations and clearly distinguish them from modern profiles; suite totals and configuration examples are never presented as certification or evidence for unexecuted requirements.

## Dependencies and related deliverables

#76, #107, #112, #128

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

The audit baseline passed622 unit/component and86 integration tests on net10Linux, but no browser interoperability or OpenID certification suite was executed; this is not conformance evidence for every profile.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage J. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://openid.net/specs/openid-connect-core-1_0.html; https://www.rfc-editor.org/rfc/rfc9700.txt; https://www.rfc-editor.org/rfc/rfc10017.txt

## Source locations

- [tests/Schemata.Authorization.Tests](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/tests/Schemata.Authorization.Tests)
- [tests/Schemata.Authorization.Integration.Tests](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/tests/Schemata.Authorization.Integration.Tests)
- [docs/documents/authorization.md](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/docs/documents/authorization.md)
- [docs/guides/authorization.md](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/docs/guides/authorization.md)
- [docs/cookbook/oidc-server.md](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/docs/cookbook/oidc-server.md)

## Change

Create a requirement→wire scenario→implementation→application-responsibility matrix per enabled profile. Exercise substantive boundary/state/concurrency combinations on compliant isolated targets and update real configuration docs. Avoid placeholder tests and unearned certified/comprehensive claims.

## Acceptance criteria

- [ ] Documented configurations run with actual DI registrations and per-app Permissions.
- [ ] Browser iframe/form_post/CORS and external RP scenarios have recorded results.
- [ ] Modern versus relaxed historical profile compliance limits are explicit.
- [ ] No suite pass is substituted for unexecuted spec requirements.

## Dependencies

None beyond existing framework capabilities.

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:interop-evidence -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #112 — Authorization: document and verify a working browser OAuth deployment profile

## Goal (X)

Provide an executable browser configuration whose cross-origin requests, nonce handling and modern enforcement compose correctly.

## X–Y analysis

A reusable CORS feature plus an emitted DPoP-Nonce header does not make the header readable by browser JavaScript. Keep this concrete deployment/configuration acceptance separate from110's broad evidence matrix; no second CORS subsystem or universal capability gate is needed.

## Canonical responsibility

Host CORS configuration plus Authorization DI-selected flow/enforcement composition and an actual browser scenario.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior9f2fac9 evidence: Core/Features/SchemataCorsFeature.cs:24-40 wires host CORS; SchemataAuthenticationHandler.cs:414-432 writes DPoP-Nonce; guide/cookbook lack executable nonce-exposure browser evidence.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No browser requests were executed; acceptance describes the retained requested profile, not a fresh standards-certification conclusion.

## Acceptance criteria

- [ ] Publish a runnable origin/method/header/exposed-header configuration using the existing CORS feature; cross-origin token requests and JavaScript reads of DPoP-Nonce work with that configuration.
- [ ] The documented modern browser profile enforces PKCE and does not return access tokens in authorization callbacks; configured refresh/front-channel behavior is explicit.
- [ ] Historical relaxed flows remain explicit opt-ins and are not described as modern-compliant; standard client metadata, not obsolete g:/r:/s: metadata permission checks, controls advertised flow metadata.
- [ ] Record browser-observed success and denial behavior on a compliant isolated target and link it into110 rather than substituting server-only tests or adding another CORS implementation.

## Dependencies and related deliverables

#79, #107, #110

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

Browser interoperability requires endpoint CORS/DPoP-Nonce exposure and compatible PKCE/refresh/front-channel policies; the framework already has a reusable CORS feature.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage C/E. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://www.rfc-editor.org/rfc/rfc10017.txt §§6.3.2–6.3.3,7.2; https://www.rfc-editor.org/rfc/rfc9449.txt §8

## Source locations

- [src/Schemata.Authorization.Foundation/Authentication/CodeFlowOptions.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Authentication/CodeFlowOptions.cs)
- [src/Schemata.Core/Features/SchemataCorsFeature.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Core/Features/SchemataCorsFeature.cs)
- [docs/cookbook/oidc-server.md](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/docs/cookbook/oidc-server.md)

## Change

Document executable DI-first composition and add behavioral verification, not a second CORS subsystem or universal capability gate. Preserve historical opt-in modes while distinguishing incompatible deployment profiles.

## Acceptance criteria

- [ ] Cross-origin token calls and DPoP-Nonce reads work with documented configuration.
- [ ] Modern browser profile requires PKCE and does not return access tokens through authorization callbacks.
- [ ] Relaxed historical configurations are not described as modern BCP compliant.

## Dependencies

- #79
- #107

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:browser-profile -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #114 — Authorization: validate the signed CIBA authentication-request profile

## Goal (X)

Authenticate and bind signed CIBA request parameters with the profile's required claims, replay protection and endpoint-aware client authentication.

## X–Y analysis

Ordinary JAR validation is not automatically the signed CIBA profile; its encryption and parameter-placement rules differ. Goal is sound and independent of the core lifecycle and general client-channel root.

## Canonical responsibility

CIBA signed-request validator using shared JOSE/replay primitives, separately from endpoint client authentication.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

No CIBA validator/feature/model is present; RequestObjectReader.cs:61-255 implements ordinary JAR only. Fresh body and comments explicitly preserve all signed-profile and audience criteria.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Prior primary/profile verification reused; no signed CIBA request or replay executed.

## Acceptance criteria

- [ ] A valid asymmetrically signed CIBA request validates iss/aud/exp/iat/nbf/jti and reaches the shared CIBA lifecycle.
- [ ] Missing required claims, invalid signatures, replay or outer duplicated authentication-request parameters fail; only client-authentication fields remain outside the request JWT.
- [ ] requested_expiry accepts the profile's number or string forms without losing validation.
- [ ] Client authentication accepts applicable issuer/token/backchannel endpoint audience identities while request JWT validation remains distinct.
- [ ] Encrypted authentication requests are rejected rather than advertised as a supported CIBA capability.

## Dependencies and related deliverables

#36, #131, #22, #131, #83, #118

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

CIBA signed authentication requests require a dedicated profile, not a blind reuse of ordinary JAR policy.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage H. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://openid.net/specs/openid-client-initiated-backchannel-authentication-core-1_0.html §§7.1.1–7.2

## Source locations

- [src/Schemata.Authorization.Foundation/Services/ClientAssertionChannel.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/ClientAssertionChannel.cs)
- [src/Schemata.Authorization.Foundation/Services/ClientAssertionValidator.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/ClientAssertionValidator.cs)
- [src/Schemata.Authorization.Foundation/Features](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Features)
- [src/Schemata.Authorization.Skeleton/Models](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Skeleton/Models)

## Change

Use existing JOSE primitives with asymmetric signatures and required iss/aud/exp/iat/nbf/jti. All authentication request parameters stay in the JWT except client-authentication fields; accept requested_expiry as number or string. Encrypted CIBA authentication requests are not supported by this spec.

## Acceptance criteria

- [ ] Correct signed CIBA request succeeds.
- [ ] Missing required claims, replay, invalid signature or outer duplicated authentication parameters fail.
- [ ] Client authentication accepts applicable issuer/token/backchannel endpoint audience identifiers.
- [ ] Encrypted authentication request is rejected rather than advertised as CIBA capability.

## Dependencies

- #36
- #70
- #22

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:ciba-signed -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #115 — Authorization: support opt-in resource step-up challenges and AS enforcement

## Goal (X)

Let resource servers request stronger/fresher authentication and make the authorization server satisfy or explicitly reject that requirement.

## X–Y analysis

Merely exposing acr/max_age inputs or copying authentication claims does not implement the challenge/reauthentication loop. Conversely, globally converting voluntary OIDC acr requests to mandatory requirements would change unrelated behavior.

## Canonical responsibility

Consumer-supplied resource authentication requirement evaluation and protocol challenge rendering, composed with AS interaction/evidence enforcement.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

SchemataAuthenticationHandler.cs:414-466 renders ordinary Bearer/DPoP errors without these challenge parameters. Source inventory finds no insufficient_user_authentication/unmet_authentication_requirements implementation; AdviceClaimsAuthenticationContext only supplies optional claims.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No RS-to-AS step-up flow executed; claims presence is not treated as implementation evidence.

## Acceptance criteria

- [ ] An opted-in RS returns 401 insufficient_user_authentication with applicable acr_values/max_age.
- [ ] Genuine reauthentication yields a satisfying token or the selected unmet_authentication_requirements error.
- [ ] Refresh/renewal does not pretend to create a new auth_time; actual authentication evidence remains authoritative.
- [ ] Ordinary voluntary acr requests retain their semantics outside this explicit requirement policy.

## Dependencies and related deliverables

#91, #106, #87

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

acr/max_age inputs and context claims exist, but insufficient_user_authentication challenges and the selected unmet-authentication policy are missing.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage C. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://www.rfc-editor.org/rfc/rfc9470.txt §§3–7

## Source locations

- [src/Schemata.Authorization.Foundation/Authentication/SchemataAuthenticationHandler.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Authentication/SchemataAuthenticationHandler.cs)
- [src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeClaims.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeClaims.cs)
- [src/Schemata.Authorization.Foundation/Advisors/AdviceClaimsAuthenticationContext.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Advisors/AdviceClaimsAuthenticationContext.cs)
- [src/Schemata.Security.Foundation](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Security.Foundation)

## Change

Provide a protocol renderer for consumer-supplied resource authentication requirements and applicable AS behavior. Use DI/advisors; do not globally convert voluntary OIDC acr requests into mandatory requirements.

## Acceptance criteria

- [ ] RS returns401 with insufficient_user_authentication and relevant acr_values/max_age.
- [ ] Reauthentication produces a token satisfying the requirement or the selected unmet_authentication_requirements error.
- [ ] Renewal does not fake a new auth_time.

## Dependencies

- #91
- #106
- #87

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:stepup -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #118 — Authorization: derive CIBA pairwise sectors from mode-specific authority

## Goal (X)

Produce stable, privacy-preserving pairwise subjects from the correct registered sector and demonstrated key ownership for each CIBA mode.

## X–Y analysis

Redirect-URI sector logic does not represent backchannel clients. Goal is sound; CIBA sector/key proof is independently deliverable from ordinary sector validation #78 and signed-request mechanics #114.

## Canonical responsibility

CIBA client registration/sector validation and mode-aware input to the existing subject projection service.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

SubjectIdentifierService.cs:60-71 uses explicit sector host or first redirect host; RegistrationMetadataMapper.cs:300-342 checks RedirectUris membership. No CIBA delivery-mode or notification metadata implementation exists.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No pairwise registration or cross-mode subject scenario executed.

## Acceptance criteria

- [ ] Poll/ping pairwise registration requires the applicable valid jwks_uri and demonstrated private-key ownership.
- [ ] When an explicit sector document applies, it contains the full required jwks or notification URI, not just an equal host.
- [ ] Push derives its sector from the notification endpoint where applicable and respects explicit sector_identifier_uri.
- [ ] Cross-mode subjects follow registered sector policy consistently without using ordinary redirect fallback for backchannel-only clients.

## Dependencies and related deliverables

#36, #78, #114, #109

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Problem and evidence

Ordinary redirect-URI sector calculation does not implement CIBA's poll/ping and push sector rules.

Evidence baseline: `87ba3e4eba7ae5ecb15da58efb215c9921d85b78`. Derived from `docs/issues/2026-09-14-authorization-spec-progress-and-plan.md`, Stage H. The local report is not assumed to be published remotely; the actionable scope is reproduced here. These are source-analysis findings or planned capabilities, not claims that a dedicated reproduction has already failed.

## Normative reference

https://openid.net/specs/openid-client-initiated-backchannel-authentication-core-1_0.html §4

## Source locations

- [src/Schemata.Authorization.Foundation/Services/SubjectIdentifierService.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/SubjectIdentifierService.cs)
- [src/Schemata.Authorization.Foundation/Services/PairwiseSubjectTranslator.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/PairwiseSubjectTranslator.cs)
- [src/Schemata.Authorization.Foundation/Services/RegistrationMetadataMapper.cs](http://raid.tekoki.internal:9000/cyp/Schemata/src/commit/87ba3e4eba7ae5ecb15da58efb215c9921d85b78/src/Schemata.Authorization.Foundation/Services/RegistrationMetadataMapper.cs)

## Change

For poll/ping use registered jwks_uri with demonstrated private-key possession and applicable sector document membership; for push use notification endpoint, respecting explicit sector_identifier_uri. Reuse the subject projection service.

## Acceptance criteria

- [ ] Poll/ping pairwise registration requires valid jwks_uri and proof of key ownership.
- [ ] Sector document lists the full required jwks/notification URI.
- [ ] Push sector derives from the notification endpoint where applicable.
- [ ] Cross-mode subject behavior follows registered sector policy.

## Dependencies

- #36
- #78
- #114
- #109

## Constraints and verification

Modes, including historical modes, are opt-in through Use* DI registration. Most enforcement is enabled by default with Relax* opt-out; prefer DI registration/runtime behavior and, when necessary, typed AdviceContext flags. Do not add a universal second global-capability gate. Active handlers/advisors check their relevant per-application e:/g:/r:/s: Permissions. SchemataApplication uses standard client metadata except Permissions and required entity fields. Preserve consumer-owned resource Name, existing resource JSON/SnakeCaseLower dictionary conventions, and one implementation without compatibility aliases. No startup scans. This issue synchronization changes no code.

Verify this behavior with focused consumer-visible scenarios on a compliant isolated target. Existing baseline: 622 unit/component and 86 integration tests passed on net10.0/Linux; no certification or browser-conformance claim. Build changed code once before --no-build runs; do not repeat test commands just to collect logs.

<!-- schemata-authorization-audit:ciba-pairwise -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #123 — SCIM: implement one shared HTTP protocol with schema-aware queries and atomic PATCH

## Goal (X)

Provide interoperable SCIM provisioning wire semantics that domain adapters can reuse without duplicating persistence or weakening existing resource APIs.

## X–Y analysis

Goal is sound; keep independent because ordinary resource JSON/filter/error conventions are not SCIM and domain adapters124-126 do not replace the shared protocol. SCIM owns interpretation and wire behavior, not Identity/Authorization persistence.

## Canonical responsibility

Shared Schemata.Scim HTTP/schema/discovery/filter/path/PATCH/error layer, explicit registrations and domain-owned transactional operations.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior9f2fac9 source inventory finds no SCIM implementation. docs/superpowers/specs/2026-09-14-remediation/scim.md:60-68 defines shared protocol/domain separation. Primary RFC7643 and RFC7644 texts were read; RFC7644 pagination table and3.5.2 require the listed pagination/atomicity behavior. Sources:https://www.rfc-editor.org/rfc/rfc7643.txt and https://www.rfc-editor.org/rfc/rfc7644.txt.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Protocol/source analysis only; no SCIM implementation, HTTP probes or conformance tests executed.

## Acceptance criteria

- [ ] Support configurable base path default/scim/v2, application/scim+json, GET/list/POST/PUT/PATCH/DELETE and resource POST .search; discovery exposes exactly installed ResourceTypes/Schemas/capabilities, without a SCIM gRPC surface.
- [ ] Honor schema-aware case-insensitive attribute names, exact extension URI keys, correct JSON types, attributes/excludedAttributes and SCIM-shaped errors including authentication/validation/not-found/uniqueness; never expose secrets.
- [ ] Support typed RFC7644 filters and Group member valuePath rather than an AIP-filter alias; invalid filters return invalidFilter; count0/negative count yields no resources, startIndex below1 is treated as1, and empty results return200.
- [ ] Apply ordered add/remove/replace PATCH atomically to one resource, restoring original state after any operation failure; PUT ignores readOnly input and does not create missing resources, while internal optimistic concurrency remains active.
- [ ] Use configurable defaults page100/max200, body1MiB, filter8KiB/depth32 and PATCH100 operations; reject excess rather than truncating then executing; unknown optional attributes follow extensibility rules and unsupported password writes return400 invalidValue without logging/echoing secrets.
- [ ] Keep Bulk/sorting/password provisioning/Enterprise extension/Me/root cross-type search/cursor/events outside this minimum; advertise unsupported flags only where defined, set ETag support false and omit meta.version without changing native Schemata JSON/error conventions.

## Dependencies and related deliverables

#124, #125, #126, #127

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Goal and scope

Implement one shared Schemata.Scim protocol implementation consumed by Identity.Scim and Authorization.Scim. Standard SCIM is HTTP/JSON only; no SCIM gRPC package. RFC7642 is informational; RFC7643/7644 provide schema/protocol contracts. Support configurable base path (default /scim/v2), application/scim+json, schema-aware case-insensitive attributes, exact schema URI keys, numeric pagination and SCIM Error status as string. Implement resource GET/list/POST/PUT/PATCH/DELETE, POST resource .search, attributes/excludedAttributes, ServiceProviderConfig/ResourceTypes/Schemas. Filtering uses RFC7644 operators and typed paths over supported attributes; include members valuePath support for Group PATCH, no AIP-filter syntax alias. PATCH add/remove/replace is ordered and resource-atomic. PUT ignores readOnly input and does not create. Bulk, sorting, password changes, Enterprise extension, /Me, root cross-type search, cursor pagination and events are outside this minimum; advertise false only where ServiceProviderConfig defines a flag. ETag support initially false; internal optimistic concurrency remains.

Acceptance: HTTP JSON casing/types/envelopes; count=0/negative and startIndex<1 rules; empty results200; projection never reveals secrets; supported filters and invalidFilter; sequential PATCH rollback; discovery exactly matches installed resource types; unsupported operations explicit, no silent fallback. All errors remain SCIM-shaped including auth/validation/not-found/unique failures. Default page100 maximum200 and configurable body/operation limits.

Sources: RFC7643 §§2-7; RFC7644 §§2-4,5-7; current Resource HTTP serializers use Schemata resource naming/error conventions and cannot serve SCIM unchanged. Shared implementation owns protocol only, not domain persistence.

## Dependencies

Existing framework capabilities only.

## Exact minimum limits

Defaults: page size100/max200, request body1MiB, filter8KiB/depth32, PATCH100 operations, all configurable. Do not truncate and execute. Ignore unknown optional schema attributes under SCIM extensibility rules, but reject explicitly unsupported password writes with400 invalidValue; never log/echo secret values. ETag disabled means meta.version omitted.

## Planning constraints

Research/specification only in the current change. Preserve one domain owner, consumer-supplied resource Name, independent authenticated provisioning-domain/tenant authorization, existing resource JSON conventions outside SCIM, and no startup scans or compatibility aliases. Local spec: docs/superpowers/specs/2026-09-14-remediation/scim.md.

<!-- schemata-scim:protocol -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #124 — Identity.Scim: provision Users through shared Identity admission and transactions

## Goal (X)

Allow authorized provisioning clients to manage minimal Users without bypassing Identity validation, uniqueness, admission or deletion rules.

## X–Y analysis

SCIM User create is administrative provisioning, not self-registration/sign-in; administrative active is not temporary lockout. Keep independent from123 because domain admission, durable binding and transactional mutation are Identity responsibilities, not protocol parsing.

## Canonical responsibility

Identity-owned provisioning mutation/admission lifecycle and durable provisioning bindings, exposed by an explicit TUser/TRole SCIM adapter.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior9f2fac9 evidence: SchemataUser lacks Active/binding metadata; Authorization.Identity/IdentitySubjectProvider.cs:80-82 validates existence only; Identity store Create/Update commit independently and Delete joins dependent repositories. RFC7643 groups readOnly and externalId provisioning scope, and RFC7644§5 PRECIS text were read: https://www.rfc-editor.org/rfc/rfc7643.txt ; https://www.rfc-editor.org/rfc/rfc7644.txt.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No SCIM/native admission, custom-store, Unicode or transaction tests executed; all feature acceptance remains open.

## Acceptance criteria

- [ ] Expose minimal schemas/id/externalId/meta/userName/displayName/active and read-only groups through full User lifecycle; other optional/Enterprise/password provisioning remains unsupported and secret/hash/stamp/token data never appears.
- [ ] Keep server id stable across rename/transports using tenant/type/entity Uid; scope durable externalId binding to the authenticated provisioning domain, prevent unauthorized adoption by displayName/userName, and preserve consumer-advisor Name generation.
- [ ] Use UserManager/store validation and normalization without issuing cookies/tokens or auto-confirming email; SCIM and native uniqueness/comparison follow the selected PRECIS profile consistently, with RFC7613/successor8265 handling documented.
- [ ] Persist administrative Active separately from lockout; deactivation changes security stamp and blocks new sign-in/refresh/issuance through shared Identity admission including Authorization.Identity, while reactivation does not clear lockout and cookie validation cadence is not misrepresented as instant revocation.
- [ ] PUT/PATCH multi-operation changes and dependency-aware delete plus binding removal are atomic and observable from a fresh scope; custom stores remain supported, and lack of required transaction capability fails the operation instead of partial success.

## Dependencies and related deliverables

#123, #125, #127

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Goal and scope

Add Schemata.Identity.Scim with explicit TUser/TRole selection. Expose /Users over shared SCIM HTTP protocol. Minimum User attributes: schemas,id,externalId,meta,userName,displayName,active,read-only groups. Other optional core/Enterprise profile attributes and password provisioning are not supported or advertised in this minimum. Server id derives from stable tenant/resource-type/entity Uid identity, never userName/client externalId or mutable Name. ExternalId is scoped to authenticated provisioning domain and stored in durable binding metadata, without duplicating user credentials/profile. Consumer repository add advisor still supplies Schemata Name.

User creation is administrative provisioning, not AuthenticateController.Register self-registration/sign-in. Reuse UserManager validation/normalization/store behavior; no cookie/token issuance or auto-confirmed email. Persist administrative Active separately from lockout; active=false blocks new sign-in/refresh through shared Identity admission and updates security stamp, reactivation must not reset independent lockout. Existing cookie validation cadence is not instantaneous global session revocation. Delete uses existing dependency-aware Identity deletion and removes binding in same unit of work. PRECIS username comparison per RFC7644§5/RFC7613 (and explicitly documented successor8265 handling) is not equivalent to naive ToUpper; normalization/uniqueness cannot disagree across SCIM and native management.

Acceptance: create/read/filter/replace/patch/deactivate/reactivate/delete from fresh scope; stable id across rename and transports; readonly groups; duplicate normalized userName; unknown/unauthorized provisioning domain cannot adopt existing users by matching displayName; atomic multi-op rollback; no password/hash/stamp/token leakage. Preserve custom user stores; missing transactional capability fails operation, not partial success.

Evidence: SchemataUser.cs has Uid/userName and no Active/externalId; IdentityUserStore Create/Update commit individually and Delete joins dependent repositories; IdentityOperationHandler.Register signs in and is not provisioning owner.

When Authorization.Identity is installed, IdentitySubjectProvider.ValidateAsync (currently existence-only at src/Schemata.Authorization.Identity/IdentitySubjectProvider.cs:80-82) must consume the same administrative Active admission. OAuth refresh/new issuance must not bypass deactivation; preserve documented cookie/security-stamp validation cadence.

## Dependencies

- #123

## Planning constraints

Research/specification only in the current change. Preserve one domain owner, consumer-supplied resource Name, independent authenticated provisioning-domain/tenant authorization, existing resource JSON conventions outside SCIM, and no startup scans or compatibility aliases. Local spec: docs/superpowers/specs/2026-09-14-remediation/scim.md.

<!-- schemata-scim:users -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #125 — Identity.Scim: provision authorized role-backed Groups atomically

## Goal (X)

Let provisioning clients manage explicitly delegated Groups and memberships without turning SCIM into privilege escalation or a second group database.

## X–Y analysis

A SCIM membership is a real role grant, and looping over individually committing AddToRole/RemoveFromRole calls cannot satisfy resource-atomic PATCH. Keep independent from User provisioning and shared protocol because role ownership and membership transactions are separate domain deliverables.

## Canonical responsibility

Identity role/membership mutation owner shared by native management and SCIM; SCIM Groups map only explicitly provisionable roles.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior9f2fac9 evidence identifies Identity/Stores/SchemataUserStore.cs:810-858 committing each membership add/remove separately and no Group adapter. RFC7643§4.2 and RFC7644§3.5.2 are the read primary schema/protocol basis: https://www.rfc-editor.org/rfc/rfc7643.txt ; https://www.rfc-editor.org/rfc/rfc7644.txt.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Static domain/protocol analysis; no membership rollback, privilege or provider tests executed.

## Acceptance criteria

- [ ] Expose full Group lifecycle with schemas/id/externalId/meta/displayName/members; existing roles require explicit administrator ownership/allowlist, and create requires role-provisioning permission without inheriting role claims or adopting privileged same-name roles.
- [ ] Require permitted target role/users within tenant and provisioning boundaries for every membership grant; reject nonexistent, cross-tenant and nested-Group members and validate value/$ref/type consistency without HTTP fetching.
- [ ] Support add/remove/replace sets and members[value eq ...] paths; duplicate add is idempotent, User.groups is derived/readOnly, and Group PUT replaces the complete explicitly managed membership set rather than guessing partial ownership.
- [ ] Use one real domain transaction for ordered PATCH/PUT/delete and native equivalent actions; any mid-operation failure restores the original group/memberships and deletion cleans dependent links.
- [ ] Display-label changes preserve stable id and consumer-owned canonical Name/permission vocabulary; SCIM cannot write role claims or arbitrary permissions.

## Dependencies and related deliverables

#124, #123, #127

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Goal and scope

Expose /Groups in Identity.Scim, mapping explicitly authorized roles to Group, never a parallel group store. Group fields schemas,id,externalId,meta,displayName,members (User references only); nested Groups excluded. User.groups is derived/readOnly. Existing roles require explicit administrator allowlist/ownership mapping; SCIM Group create requires create-role provisioning authorization and creates a role with no role claims, never adopts privileged same-name role. Display label changes do not rename the consumer-owned canonical resource Name or silently change permission vocabulary.

Membership is a real role grant: require group-management permission and permitted target role/users within tenant/provisioning boundary. PATCH value paths include members[value eq "..."]; value/$ref/type consistency validated without HTTP fetching. Group PUT replaces only the represented explicitly managed memberships; this minimum requires complete ownership of a provisioned group, not untracked partial membership merging. Role claims/permissions are not SCIM Group writable fields.

Acceptance: create/get/list/replace/delete; add/remove/replace member sets; duplicate add idempotent; invalid/nonexistent/cross-tenant/nested-group member rejection; ordered PATCH restores original resource on any failure; unmanaged/admin role denied; group deletion cleans links; no partial membership commits. Existing SchemataUserStore.AddToRoleAsync/RemoveFromRoleAsync commit each call, so add a real domain transaction boundary shared by managers/SCIM/native management rather than calling them in a loop or suppressing commit errors.

References: RFC7643§4.2 and RFC7644§3.5.2.

## Dependencies

- #124

## Planning constraints

Research/specification only in the current change. Preserve one domain owner, consumer-supplied resource Name, independent authenticated provisioning-domain/tenant authorization, existing resource JSON conventions outside SCIM, and no startup scans or compatibility aliases. Local spec: docs/superpowers/specs/2026-09-14-remediation/scim.md.

<!-- schemata-scim:groups -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #126 — Authorization.Scim: provision Applications through authoritative metadata and protected policy

## Goal (X)

Provision OAuth applications through SCIM without duplicating registration state, exposing credentials or granting administrative privileges through ordinary metadata writes.

## X–Y analysis

SCIM is a privileged provisioning adapter, not DCR/RAT management or raw resource CRUD. Update obsolete assumptions that standard GrantTypes/ResponseTypes/Scope must also be authorized by g:/r:/s: metadata permissions:9f2fac9 makes standard metadata authoritative. Separate protected framework policy remains protected.

## Canonical responsibility

Shared Authorization application metadata validation/mutation/deletion owner, with a custom SCIM Application schema and independently authorized Permissions extension.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

No Authorization.Scim implementation in prior9f2fac9 inventory. Metadata authority changed at9f2fac9; RegisterHandler delete and SchemataApplicationManager delete are not a shared atomic lifecycle. scim.md:140-149 specifies the adapter/schema projection. Primary custom-schema/protocol texts:https://www.rfc-editor.org/rfc/rfc7643.txt and https://www.rfc-editor.org/rfc/rfc7644.txt.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Source/spec analysis only; metadata integration is dependency progress, not SCIM completion or atomic-deletion proof.

## Acceptance criteria

- [ ] Expose full Application SCIM lifecycle at/Applications using project-owned Application and ApplicationAuthorization schema URIs under https://github.com/Cyprincess/Schemata/scim/schemas/2.0/; discovery matches them and native HTTP/gRPC management remains intact.
- [ ] Map authoritative standard public metadata with explicit camelCase schema types/mutability, and verify changes affect actual OAuth consumption; keep stable SCIM id distinct from immutable domain-generated clientId, consumer-owned Name and provisioning-scoped externalId binding stored outside SchemataApplication.
- [ ] Ordinary provisioning cannot mutate protected Permissions; separately authorized callers may grant only allowed policy. Explicit unauthorized permission changes reject the whole operation, while PUT without permission-write authority preserves protected fields; declared standard metadata does not synthesize admin permissions.
- [ ] Project SCIM jwks as a top-level multi-valued complex public-key attribute mapped to native OAuth {keys:[...]}, not nested jwks.keys complex fields; never expose private/symmetric keys, secrets, RATs or tokens, and issue/rotate credentials through existing security management.
- [ ] Share transactional mutation and deletion with existing domain lifecycle, including client/credential/supported-token invalidation from111; no Application active boolean or second registration store is introduced, and failures cannot partially commit metadata/policy/bindings.

## Dependencies and related deliverables

#123, #76, #111, #120, #127

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Goal and scope

Add Schemata.Authorization.Scim; custom ResourceType Application at /Applications with application-owned schema URI (not urn:ietf standard User/Group). Reuse #76 standard OAuth/OIDC public client metadata as authoritative application fields, expose camelCase SCIM representation and SCIM common metadata. No second OAuth registration store; SCIM is privileged provisioning, not RFC7591/7592 dynamic registration and uses provisioning auth, not RAT. Server SCIM id distinct from client_id/Name/externalId. clientId immutable after creation, generated through existing domain behavior on create; resource Name remains consumer advisor supplied. Durable externalId/provisioning-domain binding stored outside SchemataApplication so #76 standard metadata constraint holds.

Permissions is an explicit Schemata schema extension, writable only with separate permissions-management authorization and allowed target grants; ordinary metadata provisioning cannot raise e:/g:/r:/s: privileges. PUT without permission-write authority preserves protected fields outside caller write scope; explicit unauthorized permission mutation rejects entire operation. jwks contains public material only. Secrets/private keys/RAT/tokens absent from schema and never returned; credentials issued/rotated through existing security management, not SCIM response. No Application active boolean in minimum; deprovision by delete.

Acceptance: full SCIM create/get/list/PUT/PATCH/delete; public metadata readback and actual OAuth consumption; exact schema/ResourceType discovery; normal caller cannot write Permissions, privileged policy-limited caller can; no auto-grants from declared grantTypes/scopes; transactional rollback; deletion atomically invalidates client/credentials and associated supported tokens via shared existing domain lifecycle. Preserve native Application HTTP/gRPC management, consolidate changed mutation validation/commit owners instead of bypassing invariants with raw resource CRUD.

References: RFC7643§§3.1-3.3,6-7 (custom type); RFC7644 operations. Depends on metadata #76 and shared protocol; existing manager setters merely assign and Create/Update/Delete commit.

## Dependencies

- #123
- #76
- #111 — shared client/credential/token deletion lifecycle

## Custom schema wire contract

Application schema URI: https://github.com/Cyprincess/Schemata/scim/schemas/2.0/Application. Permissions extension URI: https://github.com/Cyprincess/Schemata/scim/schemas/2.0/ApplicationAuthorization. Definitions are returned through /Schemas; these are project-owned identifiers, not IETF/IANA registrations. SCIM jwks is a top-level multi-valued complex public-key attribute, mapped to the native OAuth {"keys":[...]} object. Do not nest jwks.keys complex subattributes: RFC7643 section2.3.8 prohibits nested complex subattributes. Native OAuth JWKS representation remains unchanged.

## Planning constraints

Research/specification only in the current change. Preserve one domain owner, consumer-supplied resource Name, independent authenticated provisioning-domain/tenant authorization, existing resource JSON conventions outside SCIM, and no startup scans or compatibility aliases. Local spec: docs/superpowers/specs/2026-09-14-remediation/scim.md.

<!-- schemata-scim:applications -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #127 — SCIM: publish exercised provisioning interoperability and supported limits

## Goal (X)

Demonstrate that the shared SCIM protocol and domain adapters work together for a precisely stated provisioning profile.

## X–Y analysis

Goal is sound; keep independent because implementing123-126 does not produce real provider/client interoperability evidence or user documentation. This acceptance work must not grow a parallel SCIM implementation or become generic OAuth evidence110.

## Canonical responsibility

End-to-end SCIM HTTP/provider/client verification and capability documentation, consuming implementation owners123-126 and packaging67.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

Prior inventory has no SCIM implementation/tests; docs/superpowers/specs/2026-09-14-remediation/scim.md:151-162 lists future verification and explicitly states no protocol probes/tests. Fresh issue127 preserves both providers and external-client acceptance.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** No execution in this assignment; no SCIM interoperability, browser or certification claim.

## Acceptance criteria

- [ ] Exercise real HTTP User/Group/Application lifecycle against local SQLite EFCore and LinqToDB repositories with explicit consumer Name advisors and fresh-scope persistence readback.
- [ ] Verify installed discovery, schema/extension URI/casing/types, stable ids and provisioning-domain externalId scope, numeric pagination/filter/projection and SCIM errors, including userName eq and Group member valuePath used by real clients.
- [ ] Demonstrate rollback on mid-PATCH failure and provider concurrency, membership/Permissions privilege boundaries, delete cleanup, administrative Active admission and secret redaction.
- [ ] Roundtrip the same entities through native management HTTP/gRPC and SCIM where applicable, proving shared domain behavior rather than separate stores.
- [ ] Record an external provisioning-client transcript for the exact supported profile; publish executable configuration, migration, target-switch guidance and explicit Bulk/sort/password/nested-group/Enterprise/ETag/cursor/events limits without vendor-wide or certification claims.
- [ ] Keep tests consumer-visible state/wire/error focused and report exact executed scope; implementation presence or unit totals cannot replace unexecuted interoperability requirements.

## Dependencies and related deliverables

#123, #124, #125, #126, #67, #128

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Goal and scope

Verify completed SCIM protocol and both adapters with real HTTP against actual repositories using local SQLite EFCore/LinqToDB; explicit consumer Name advisors. No claim of certification or third-party compatibility without exercised evidence.

Acceptance: discovery matrix, schemas/ids/externalId scope, full User/Group/Application lifecycle, attribute case/URI namespace and JSON types, pagination/filter/projection, atomic PATCH rollback under mid-sequence error and provider concurrency, role privilege restrictions, Permissions authorization, delete cleanup, user active admission, secret redaction, native management HTTP/gRPC roundtrip against same entities. Include an external provisioning client transcript for exactly supported profile; unsupported Bulk/sort/password/nested groups/Enterprise/ETag/cursor/events remain explicit. Verify query userName eq and group member valuePath used by provisioning clients, not assume every vendor accepts supported subset.

Update component documentation, target opt-in switches via #67, example configuration and migration. Source tests must assert consumer-visible state/wire/errors, no source-text tests. Build changed projects once, then --no-build; do not rerun identical commands only to save logs. This issue is acceptance and documentation, not a parallel implementation.

## Dependencies

- #123
- #124
- #125
- #126

## Planning constraints

Research/specification only in the current change. Preserve one domain owner, consumer-supplied resource Name, independent authenticated provisioning-domain/tenant authorization, existing resource JSON conventions outside SCIM, and no startup scans or compatibility aliases. Local spec: docs/superpowers/specs/2026-09-14-remediation/scim.md.

<!-- schemata-scim:interop -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #128 — Transport: publish operation-level management parity and protocol exceptions

## Goal (X)

Give consumers an accurate account of remote surfaces and verified HTTP/gRPC semantic parity without inventing proprietary copies of HTTP standards.

## X–Y analysis

A package or matching resource registration proves neither an operation exists nor that both transports behave equivalently. Keep independent from protocol/client evidence110/127 and feature tickets: the deliverable is an anchored operation matrix and ongoing parity gate, not blanket endpoint construction.

## Canonical responsibility

Published per-operation surface/verification matrix and review discipline over shared native management domain handlers; OAuth/OIDC/SCIM/browser exceptions remain protocol-owned HTTP.

## Evidence and disposition

Open: pursue the goal and acceptance below at the responsible boundary.

docs/superpowers/specs/2026-09-14-remediation/scim.md:39-58 contains a source-presence component matrix, not completed per-operation runtime evidence;151-160 retains shared-domain parity and existing feature owners. Prior evidence confirms matching native Identity/Authorization resource registrations only.

**Baseline:** both Gitea master and the local checkout were verified at `9f2fac9e4a6be62026655d727094366ec8050241`. Earlier comments referring to unpublished local fixes or `87ba3e4e` are historical.

**Verification boundary:** Partial internal documentation exists; no HTTP/gRPC parity scenarios executed and no blanket parity claim made.

## Acceptance criteria

- [ ] Publish a current per-operation matrix with registration/source anchors distinguishing native management, application actions, runtime-only services and standards-defined HTTP surfaces; separate present, absent and exercised parity.
- [ ] For changed native management APIs verify same-domain authorization, mutation, visible state and error meanings over HTTP/gRPC, and establish a dual-transport review gate without duplicating domain handlers.
- [ ] Keep OAuth/OIDC/SCIM and redirect/cookie browser flows explicitly HTTP; SCIM is an additional provisioning layer and no SCIM gRPC adapter is implied.
- [ ] Link existing Push50/57, Messaging54/55/56 and Quota59/61 deliverables instead of duplicating them; services lacking any remote management contract are not mislabeled as merely missing gRPC, and future Identity self-service gRPC requests remain separate.

## Dependencies and related deliverables

#50, #54, #55, #56, #57, #59, #61, #110, #127

Dependencies and independently testable deliverables remain separate; shared terminology alone is not a merge reason.

## Preserved constraints

The current X/Y analysis and acceptance govern implementation; historical implementation prescriptions below are retained for traceability, not competing instructions. Preserve consumer-owned resource names, existing extension boundaries, and explicit feature opt-in decisions. For Authorization, standard GrantTypes/ResponseTypes/Scope metadata is authoritative at this baseline; obsolete g:/r:/s: metadata reconstruction is not restored. No code, push, build or tests were performed by this issue-editing pass.

<details>
<summary>Prior issue body and acceptance (historical record)</summary>

## Goal and scope

Establish operation-level management/application/protocol transport matrix. Native component management APIs target HTTP+gRPC semantic parity using same domain handler, authorization, mutation and error meaning. A component without any remote management surface is not merely missing gRPC. Standards-defined HTTP protocols (OAuth/OIDC/SCIM) are explicit HTTP interfaces, not proprietary gRPC copies; browser redirect/cookie flows remain HTTP. User selected SCIM HTTP only; no new SCIM gRPC adapter in this scope.

Evidence: Identity.Http/Grpc register Users/Roles; Authorization.Http/Grpc register Application/Scope/Token; Flow/Scheduling/Report register dual resource surfaces and Insight has dual query services. Identity Foundation Authenticate/Account controllers are HTTP application actions; Authorization Foundation Connect controllers implement HTTP OAuth/OIDC. Push control both transports absent (#50/#57), Messaging streams tracked #54/#55/#56. Tenancy/Security/Actor expose runtime services but no dedicated remote management package.

Acceptance: publish factual per-operation matrix with source registration anchors, distinguish present from tested parity, retain existing Push/streaming/Quota work rather than duplicate tickets; SCIM shown as additional HTTP provisioning layer; identify any future requested Identity self-service gRPC separately without claiming OAuth gRPC standard. Add dual-transport review gate for new native management APIs and shared-domain tests when those APIs change. No blanket new endpoints or startup checks.

## Dependencies

Existing framework capabilities only.

## Planning constraints

Research/specification only in the current change. Preserve one domain owner, consumer-supplied resource Name, independent authenticated provisioning-domain/tenant authorization, existing resource JSON conventions outside SCIM, and no startup scans or compatibility aliases. Local spec: docs/superpowers/specs/2026-09-14-remediation/scim.md.

<!-- schemata-scim:transports -->
</details>

<!-- schemata-xy-backlog:2026-09-21 -->

### #196 — Transport.Http: shared RFC 8288 Web Linking model and Link header serialization

Source: maintainer direction 2026-10-03 — adopt RFC 8288 across components with HTTP endpoints. This family is tracked as standalone issues and is NOT part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope.

## Problem

Components that need to express relationships between resources — pagination neighbors, related executions, subscriptions, process instances — currently have no shared link model. Each component would invent bespoke fields or URL conventions.

## Decision

- One shared Web Linking capability per [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288.txt): a link model (context, target, relation type, target attributes) per §2 and §2.2, and serialization into the HTTP Link header field per §3.
- Relation types prefer IANA registered relation types per §2.1.1 and the §4.2 registry; extension relation types are minted as URIs per §2.1.2 only when no registered type fits.
- Relation-type string constants live in each component's Constants type (framework convention), not in ad-hoc literals.
- Inbound Link header parsing follows the Appendix B algorithms for consumers that accept links from external systems.

## Transport uniformity and AIP alignment

The same relationship data must behave identically on HTTP, gRPC, and future transports. The uniform layer is AIP-conformant; the Link header is its HTTP projection:

- Standard fields follow [AIP-148](https://google.aip.dev/148) for their own semantics: the `name` field is the resource's canonical identifier and stays the first field; `uid` when exposed is UUID4 with the format extension. AIP-148's `annotations` is an optional, consumer-writable map for small arbitrary data; it is not a carrier for service-generated links, and this family does not encode the link model into it.
- Typed relationships follow [AIP-122](https://google.aip.dev/122): resource-name reference fields annotated with google.api.resource_reference in proto models. AIP-122 forbids self-link fields on resources, so no component adds them.
- Pagination follows [AIP-158](https://google.aip.dev/158): `next_page_token` in the response body is the uniform pagination datum — opaque, carrying no authorization, and continuation calls keep all other arguments identical. This token contract (#194) is unchanged.
- HTTP expression follows [AIP-127](https://google.aip.dev/127): the prescribed verbs for standard methods (`get`/`post`/`patch`/`delete`, never `put`), URI templates that embed the full resource name rather than only its ID component, and body placement per its rules (no body on GET/DELETE; a single top-level field for Create/Update/custom-method bodies). Binding remains the framework's existing default binding — this family introduces no protobuf `google.api.http` annotations and no annotation-driven transcoding; conformance targets the observable HTTP shape only.
- RFC 8288 Link headers on HTTP responses are a projection of the same relationships and pagination state; gRPC surfaces carry the same data through the fields above or in metadata. No transport invents a third representation.

## Acceptance

- A response can carry multiple links with distinct relation types serialized per §3; round-trip parsing per Appendix B preserves them.
- Components consume the shared model; no component defines a second link serialization.
- Registered relation types are used where they exist (e.g. self, next, prev, first, last, related, canonical); extension types are URIs.
- Endpoints introduced or touched by this family keep the AIP-127 verb/URI/body shape using the framework's default binding.
- The same read on HTTP (Link headers + body) and gRPC (body/metadata) yields the same relationship data.

Per-component adoption is tracked in the linked blocked issues.

### #197 — Resource: emit RFC 8288 links for pagination and resource relationships

Blocked by the shared Web Linking capability issue. Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope.

## Problem

Resource List responses paginate with opaque page tokens (AIP-158) but expose no navigable links; a client cannot discover next/previous pages, the canonical self link, or related child collections without out-of-band knowledge.

## Decision

- List and Get responses emit Link headers per RFC 8288 §3 using the shared link model: self, next, prev, first, last for paginated collections; canonical for the resource's canonical name; related links for declared child collections.
- Links complement page tokens; the token contract (#194) is unchanged and remains the resumption authority.
- Relation types come from the IANA registry (RFC 8288 §2.1.1); resource-specific relationships use extension relation URIs per §2.1.2.

## Acceptance

- A two-page List returns a next link on page one and prev/first links on page two; following the links yields the same result set as the page-token path.
- Get returns self/canonical links matching the resource's canonical name.
- Custom-method responses can declare their own links through the same model.

### #198 — Scheduling: expose RFC 8288 links between jobs, executions and owners

Blocked by the shared Web Linking capability issue. Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope.

## Problem

A Scheduling job and its executions are separate resources; clients must construct URLs by convention to move between a job, its execution history, a single execution, and the owning domain resource.

## Decision

- Job resources emit links per RFC 8288 §3: self, related links to current/recent executions, and a link to the owning domain resource where one exists.
- Execution resources link back to their job and to the domain operation they performed.
- Registered relation types per RFC 8288 §2.1.1 are preferred; scheduling-specific relations use extension URIs per §2.1.2.

## Acceptance

- From a job representation a client reaches its executions by following links without composing URLs.
- From an execution a client reaches its job and the affected domain resource.
- Links are emitted identically on the HTTP management surface; gRPC carries the same model in metadata.

### #200 — Push: expose RFC 8288 links for subscriptions, providers and targets

Blocked by the shared Web Linking capability issue (#196). Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope. Endpoint inventory verified at HEAD 83f9a4f.

## Existing addressable targets (PushController)

- `GET /v1/push/subscriptions` (list, optional provider filter)
- `POST /v1/push/subscriptions` (create)
- `DELETE /v1/push/subscriptions` (delete by provider + providerKey)
- `POST /v1/push/subscriptions:send`

## Decision

- The subscriptions collection response emits links per RFC 8288 §3: self, and provider-filtered variants of the list.
- Links never expose provider keys or secret material (input-only per R13).

## Not applicable at HEAD (no addressable target, not invented)

- Per-subscription self links: there is no GET-single-subscription endpoint; identity is (provider, providerKey) carried in request bodies.
- Provider descriptor / capability-metadata links: no provider endpoint exists; built-in providers are future work (#190).

## Acceptance

- The list response carries a self link and, when filtered, a link to the unfiltered collection, serialized per RFC 8288 §3.

### #201 — Flow: expose RFC 8288 links between definitions, instances, tokens and catches

Blocked by the shared Web Linking capability issue (#196), whose AIP alignment section governs in-body relationship data. Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope. Endpoint inventory verified at HEAD 83f9a4f.

## Existing addressable targets

- SchemataProcess resource (with Flow method set)
- SchemataProcessToken resource (with Flow method set)
- SchemataProcessTransition resource
- ProcessDefinitionsController: definitions LIST endpoint only (no single-definition or per-version GET)

## Decision

- Process representations emit links per RFC 8288 §3: self, the filtered token list for this process, the transition list, parent/child process instances via filtered process lists where parent identity is persisted, and the definitions list (carrying the process's definition identity as a filter parameter where the list supports it).
- Token representations link back to their process.
- In-body relationship fields use AIP-122 resource-name references (e.g. a token's process name); no self-link fields.

## Not applicable at HEAD (no addressable target, not invented)

- Single-definition or definition-version links: the controller exposes only the definitions list; no per-definition GET exists.
- Waiting signal/timer catch links: there is no catches endpoint; catch state lives inside process/token representations.

## Acceptance

- From a process a client reaches its tokens, transitions, and the definitions list entry for its definition by link; from a token its process.
- Definition navigation lands on the same rows as a directly filtered definitions list call.

### #202 — Authorization: expose RFC 8288 links across Application, Scope and Token management resources

Blocked by the shared Web Linking capability issue (#196). Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope. Endpoint inventory verified at HEAD 83f9a4f.

## Existing addressable targets (Authorization.Http)

- Application resource (TApp)
- Scope resource (TScope)
- Token resource (SchemataToken)

The OAuth/OIDC protocol endpoints (Connect/*) are protocol-defined and out of this issue's scope; the per-client registration management URI is already covered by RFC 7592's own registration_client_uri field.

## Decision

- Application representations emit links per RFC 8288 §3: self, the filtered token List for this application, and the Scope List.
- Token representations link to their application and parent authorization where that association is persisted.
- Scope representations link to applications granted them via the filtered Application List.
- Links never expose secrets or raw token values.

## Acceptance

- From an application a client reaches its tokens and scopes by link, landing on the same rows as a directly filtered List call.
- From a token a client reaches its application.

### #203 — Identity: expose RFC 8288 links across User, Role and session resources

Blocked by the shared Web Linking capability issue (#196). Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope. Endpoint inventory verified at HEAD 83f9a4f.

## Existing addressable targets (Identity.Http, feature L35-36)

- User resource (TUser)
- Role resource (TRole)

## Decision

- User and Role representations emit links per RFC 8288 §3: self and canonical matching the resource's canonical name.

## Not applicable at HEAD (no addressable target, not invented)

- Session links: no session resource is registered on the HTTP surface.
- User→roles and role→members navigation: role assignment and membership are managed through store APIs; no assignment or members sub-resource routes exist.

## Acceptance

- User and Role Get/List responses carry self/canonical links consistent with each resource's canonical name.

### #204 — Insight: expose RFC 8288 links for queries, sources and result pages

Blocked by the shared Web Linking capability issue (#196), whose AIP alignment section governs in-body relationship data. Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope. Endpoint inventory verified at HEAD 83f9a4f.

## Existing addressable targets (Insight.Http)

- InsightController query endpoint only: POST with the query bound from the request body. Sources are registered in code, not exposed as HTTP resources.

## Decision

- Query responses emit links per RFC 8288 §3: self, and next/prev for paginated results.
- Because the query is body-bound, a next/prev link cannot be replayed as a bare GET. The call contract is defined explicitly: the link carries the continuation token as an RFC 8288 §2.2 target attribute, and following the link means re-issuing the same POST with the original body plus that token. AIP-158's `next_page_token` in the response body remains the authoritative pagination datum; the links are its HTTP projection, never a replacement.
- The continuation binding from #194 applies unchanged: a mismatched caller, tenant, or query argument fails as it does on the token path.

## Not applicable at HEAD (no addressable target, not invented)

- Source → public-model schema links: no source HTTP resource exists; schema travels inside the query response itself.

## Acceptance

- A paginated query result carries self and next/prev links whose continuation token attribute replays correctly under the re-POST contract and fails under a tampered or cross-caller replay, matching the token path.

### #205 — Report: expose RFC 8288 links across definitions, snapshots, chunks and schedules

Blocked by the shared Web Linking capability issue (#196). Not part of docs/superpowers/specs/2026-10-03-framework-remediation.md scope. Endpoint inventory verified at HEAD 83f9a4f.

## Existing addressable targets (Report.Http)

- Report definition resource (TReport, with ReportMethods)
- Snapshot resource (TSnapshot, with SnapshotOperations/SnapshotMethods including chunk/row reads)
- Scheduling job resource (Scheduling.Http) for the armed schedule

## Decision

- Definition representations emit links per RFC 8288 §3: self, the filtered snapshot List for this definition, and the Scheduling job resource of the active schedule.
- Snapshot representations link to their definition and to their chunk/row read methods (existing custom methods).
- Links honor the same (report, snapshot) relational key as the internal store, so reports/A/snapshots/daily and reports/B/snapshots/daily never cross-link.

## Not applicable at HEAD (no addressable target, not invented)

- A Report-side schedule resource: scheduling state lives on the Scheduling job resource; no separate Report schedule endpoint exists.

## Acceptance

- From a definition a client reaches its snapshots and the schedule job by link; from a snapshot its definition and chunk/row methods.

### #285 — Flow.Bpmn: bind call-activity completion to the current child activation

## Static review

HEAD4a30881b; no execution. CallActivityExecutor.EnterAsync writes Spawn transition identified by parent process/token+activity name. TryCompleteAsync (`:121-125`) selects FirstOrDefault from all matching transitions with no current activation discriminator. A supported flow re-entering the same call activity under the same parent token can match an old terminal child while new child is live. Existing actual child/spawn identities are sufficient basis; graph node name is not activation identity.

## Direction

Use current transition/token lifecycle to persist which spawn belongs to current activation; completion matches that child. No second child registry, completion flag carrier or duplicate engine path.

## Acceptance

Repeat call activity waits for current child; prior terminal spawn cannot resume it; atomic parent/child persistence retained.

P1 wrong process completion.

### #291 — Comment-blind lookahead misclassifies annotated typed ViewFields

## HEAD review follow-up

HEAD `0a8372e137d15af40ce9917e99eb94d714915343`; inclusive71-commit review. Static control-flow analysis only; no build/tests/smoke/spike.

The bodyTyped predicate skips only char.IsWhiteSpace at Parser.cs:426–428 before requiring '[', '{', or '=' at line 438. Entity and Document configure both line and block comments at lines 726–730 and 770–774, and docs/modeling/grammar.md:3–5 permits comments between tokens. For `Entity Example { Object response { string title /* explanation */ [omit] } }`, the predicate encounters '/' and rejects the typed alternative, allowing the untyped alternatives to represent `string` and `title [omit]` as separate fields instead of preserving the typed annotation. Object projections currently produce no C#, so the established impact is incorrect parsed projection metadata, not incorrect generated DTOs. Make this existing lookahead use the configured whitespace/comment-skipping mechanism before checking the continuation, and restore the cursor afterward. Preserve the current continuation rule; do not introduce type-name lookup or bare-typed-field acceptance. Static review only.

### #296 — Authorization-details parsing never disposes its JsonDocument

## HEAD review follow-up

HEAD `0a8372e137d15af40ce9917e99eb94d714915343`; inclusive71-commit review. Static control-flow analysis only; no build/tests/smoke/spike.

Parse creates a JsonDocument at line 52 but never disposes it on the successful return at line 89 or the validation failures at lines 59–83. Every nonblank syntactically valid input, including [], takes this ownership path; Narrow also invokes Parse for its inputs. Consequently the document's pooled storage is not returned through deterministic disposal, increasing allocation and garbage-collection pressure. The descriptor receives element.Clone() at line 81 and returned nodes are independently parsed at line 86, so no outgoing value requires the original document to remain alive. Establish a using lifetime immediately after the existing JsonException catch, covering validation and return, as Narrow already does for its documents. Preserve the current error mapping. Static review only.

### #334 — Tenancy: cookie registration writes a debug probe.log on every options build

## HEAD static review

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; introduced in `23d9d91a feat(Tenancy)!: isolate per-tenant infrastructure end to end`; no build/runtime execution.

## Evidence

`src/Schemata.Tenancy.Foundation/State/TenantCookieRegistration.cs:59`:

```csharp
System.IO.File.AppendAllText("probe.log", $"cookie-apply scheme={scheme} sessionStore={(options.SessionStore is null ? "null" : options.SessionStore.GetType().Name)} prefix={profile.TenantUid:N}{System.Environment.NewLine}");
```

This runs inside the `IConfigureOptions<CookieAuthenticationOptions>` callback, i.e. every time a tenant cookie scheme's options are built. It is not inside `#if DEBUG`, does not use `ILogger`, and writes to the process working directory.

## Impact

- Unbounded `probe.log` growth and synchronous disk I/O on an authentication configuration path.
- On read-only containers or non-writable content roots, `AppendAllText` throws and breaks authentication configuration for the tenant.
- Leaks tenant UID, scheme name, and ticket-store type to a world-readable file.
- Concurrent writes have no synchronization or rotation.

## Correction

Delete the line. If diagnosis is still needed, log through the existing `ILogger` abstraction at `Debug` level and never write to the working directory from options setup. Add a test that builds tenant cookie options in a temporary, read-only content root and verifies no file-system write is attempted.

### #336 — Entity: repository/cache suppression scopes are state flags carried through AdviceContext

## HEAD static review

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; no build/runtime execution.

## Evidence

The framework exposes operation-scoped suppression through marker objects stored in `AdviceContext` and read with `ctx.Has<T>()`:

- Public repository API: `IRepository.cs:52-79` (`SuppressAddValidation`, `SuppressUpdateValidation`, `SuppressQuerySoftDelete`, `SuppressSoftDelete`, `SuppressTimestamp`) implemented at `RepositoryBase.cs:496-500` with `AdviceContext.Use<...>()`.
- Extension API: `Entity.Repository/Extensions/OwnerExtensions.cs:19-33` (`OwnerSuppressed`, `QueryOwnerSuppressed`); `Entity.Cache/Extensions/RepositoryExtensions.cs:24-41` (`QueryCacheSuppressed`, `QueryCacheEvictionSuppressed`).
- Marker types: `AddValidationSuppressed`, `OwnerSuppressed`, `QueryOwnerSuppressed`, `QuerySoftDeleteSuppressed`, `SoftDeleteSuppressed`, `TimestampSuppressed`, `UniquenessSuppressed`, `UpdateValidationSuppressed`, plus the two cache markers.
- Advisor branches: `AdviceAddOwner.cs:58`, `AdviceAddSoftDelete.cs:40`, `AdviceAddTimestamp.cs:42`, `AdviceAddUniqueness.cs:49`, `AdviceAddValidation.cs:44`, `AdviceBuildQueryOwner.cs:54`, `AdviceBuildQuerySoftDelete.cs:40`, `AdviceRemoveSoftDelete.cs:44`, `AdviceUpdateTimestamp.cs:42`, `AdviceUpdateValidation.cs:45`, `AdviceCache` advisors at `AdviceQueryCache.cs:59` / `AdviceResultCache.cs:68` / `AdviceCommittedEvictCache.cs:45`.
- Framework call sites rely on the scopes: `FlowSourceReadScope.cs:14-15`, `ProcessPersistence.cs:256`, `AdviceValidateResourceReferenceExistence.cs:151`, `UndeleteHandler.cs:32`, `ResourceOperationHandler.Delete.cs`, `FindTenantByHostHandler.cs:19-20`, `FindTenantByIdHandler.cs:16`.

## Why this violates the framework conventions

This is exactly a state flag inside an advisor context: a capability switch that changes advisor behavior without appearing in the advisor's typed arguments, resolved output, or the DI registration. It is invisible to the consumer, inherited by every nested repository/dispatch operation on the same context, and makes equal queries/updates behave differently based on ambient mutable state. It also duplicates the DI-based mechanism the framework uses for optional stages (keyed validation advisors, enumerable advisor chains, `ResourceRegistry` stage composition).

## Correction

Replace the marker/scope mechanism with explicit operation inputs or DI-selected advisor composition, for example:

- add explicit query/mutation options (e.g., `IncludeDeleted`, `IgnoreOwner`, `SkipTimestamp`, `ValidationMode`, `CacheMode`) to the repository operations that need them;
- or resolve keyed advisor variants / separate operation methods for framework-internal bypasses;
- migrate the internal call sites listed above and remove the marker types and `AdviceContext.Use/Has` branch from each advisor.

If a scope is retained for ergonomics, it must at minimum be an explicit, typed parameter of the operation rather than an entry in the shared context bag. Add regression tests for nested calls (suppression must not leak) and for parallel operations on distinct repositories.

### #337 — Tenancy: provider cache serializes request threads and uses a ThreadStatic construction guard

## HEAD static review

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; the lease/retirement logic itself is the #330 fix; no build/runtime execution.

## Evidence

`src/Schemata.Tenancy.Foundation/Services/MemoryCacheTenantProviderCache.cs`:

- Lines 27-28: `[ThreadStatic] private static HashSet<Pending>? _constructing;` — process-wide, thread-bound guard state in a singleton cache.
- Lines 390-395: `EnterConstruction`/`ExitConstruction`/`IsConstructing` operate on that static set.
- Lines 107-119 (`LeaseVersion`): a loser of the construction race falls through to `pending.Wait()`.
- Line 442: `public void Wait() { _completion.Task.GetAwaiter().GetResult(); }` — synchronous blocking on another request's container build.

## Impact

- `Lease` is called from the tenancy middleware and from `SchemataTenantServiceScopeFactory.CreateAsync`, i.e. on request and background job threads. Waiting synchronously for a tenant container build blocks a thread-pool thread for the whole build (DI construction can be expensive), so a burst of first requests for one tenant can starve the pool; nested/cyclic construction can deadlock.
- The guard is `[ThreadStatic]` while await continuations may resume on another thread. The reentrancy it is meant to detect can therefore be missed (then `Wait()` blocks on itself) or the pending set can be observed on the wrong thread. It is also mutable static runtime state, contrary to the framework rule against static runtime state.
- No cancellation or timeout: a request abort cannot stop the wait.

## Correction

- Keep construction ownership on the per-key `Pending` entry (e.g., an owner token stored in the entry) instead of a static thread-local set, or use an async gate keyed by the entry.
- Make waiters await `pending.Completion` with the caller's cancellation token (and a bounded timeout), not `GetAwaiter().GetResult()`; if the lease API must stay synchronous, complete construction without making unrelated threads wait (reentrant build detection plus a failure path).
- Add tests for two concurrent first requests for one tenant, cancellation while waiting, and an exception during container build, asserting no thread is blocked and no provider is leaked.

### #338 — Tenancy: composition relies on fake tenant ids, concrete-type checks, descriptor removal and runtime probes

## HEAD static review

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; no build/runtime execution.

## Evidence

1. **Fake-tenant harvest** — `src/Schemata.Tenancy.Foundation/SchemataTenancyBuilder.cs:36`:
   ```csharp
   configure(new TenantRepositoryBuilder<TTenant>(new ServiceCollection(), Guid.NewGuid(), partitioned.Add));
   ```
   The consumer callback is executed an extra time with a throwaway collection and a random tenant id just to discover partitioned entity types; user code observes a false `TenantUid` and runs side effects outside any real tenant registration.
2. **Concrete evaluator check** — `src/Schemata.Tenancy.Foundation/Middlewares/TenantPolicyEvaluator.cs:23-24`:
   ```csharp
   var evaluator = inner.GetType() == typeof(PolicyEvaluator)
       ? new PolicyEvaluator(context.RequestServices.GetRequiredService<IAuthorizationService>())
       : inner;
   ```
   A custom `IPolicyEvaluator` subclass is used with whatever services it captured, while only the exact framework type is rebuilt; the branch also allocates a new evaluator per authorization call.
3. **Descriptor removal** — `src/Schemata.Tenancy.Foundation/State/TenantAntiforgeryRegistration.cs:60-68` removes **every** `IStatePipelineProvider` descriptor from the tenant collection and adds its own. The comment refers to "Task 8's session registration", and any host/custom provider registered earlier is silently discarded.
4. **Runtime probe** — `src/Schemata.Tenancy.Messaging/TenantMessagingExtensions.cs:33-39` creates a probe scope while composing tenant overrides, resolves `IRequestDispatcher`, and compares its concrete type to decide whether to alias the in-process dispatcher; it then blocks on `DisposeAsync().AsTask().GetAwaiter().GetResult()`.

## Impact

Tenant composition depends on implementation type identities, mutation of the service descriptor list, and constructing a scope during container build. Consumer customizations/providers are silently lost or bypassed, the callback contract is violated by the fake tenant id, and the probe result can change with decorator/registration order. This is the kind of cross-component hack the framework conventions prohibit.

## Correction

- Record the partitioned entity types explicitly on the builder and replay that recorded list per tenant; do not re-run the consumer callback with a fake id.
- Resolve `IPolicyEvaluator` through DI / a factory that receives the request scope, or keep the inner evaluator and inject the tenant-aware `IAuthorizationService` through a scoped service; never branch on `GetType() == typeof(...)`.
- Declare the state pipeline provider once and make capabilities contribute branches (e.g., a registry of `(pipeline, builder)` contributions) instead of removing descriptors.
- Make tenant dispatcher aliasing a registration-time contract (a keyed/optional service or a delegating decorator), not a runtime probe that creates a scope during container build.

### #342 — Tenancy: SessionCache blocks synchronously on asynchronous cache operations

## HEAD static review

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; no build/runtime execution.

## Evidence

`src/Schemata.Tenancy.Caching/SessionCache.cs` implements the synchronous `IDistributedCache` members by blocking on the asynchronous provider:

- line 31: `_cache.GetAsync(Key(key)).GetAwaiter().GetResult()`
- line 39: `_cache.SetAsync(Key(key), value, Map(options)).GetAwaiter().GetResult()`
- line 47: `_cache.RefreshAsync(Key(key)).GetAwaiter().GetResult()`
- line 55: `_cache.RemoveAsync(Key(key)).GetAwaiter().GetResult()`

The provider may be the Redis backend (network I/O). These members can be called from ASP.NET request/session code.

## Impact

Blocking a thread on network I/O can exhaust the thread pool under load and, when a synchronization context is involved, deadlock. The sync API also bypasses the async cancellation honored by the provider. The synchronous members are part of `IDistributedCache`, but the adapter chooses to implement them by blocking rather than by delegating to a synchronous backend path or documenting the constraint.

## Correction

- Prefer the asynchronous members from the session middleware path (ASP.NET Core session uses `ISessionStore` async overloads), so the blocking fallbacks are not on the request path.
- If the sync members must exist, implement them through a bounded synchronous path or a backend that supports sync operations, and document that they block; keep timeouts/cancellation bounded.
- Add a test that a slow backend does not block the calling thread beyond the configured timeout and that cancellation propagates on the async path.

### #343 — Framework hygiene: stale package directories, unused skeleton reference, byte-identical constants and mapper overload surface

## HEAD static review

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; no build/runtime execution. These are low-severity consistency findings, grouped to avoid issue noise.

## Evidence

1. **Stale package directories** — `src/Schemata.Caching.Distributed/` and `src/Schemata.Entity.Owner/` contain only `FodyWeavers.xsd` (no `.csproj`, no source). They were left behind when the packages were deleted (the Caching.Distributed deletion is visible in `23d9d91a`).
2. **Unused project reference** — `src/Schemata.Tenancy.Skeleton/Schemata.Tenancy.Skeleton.csproj` references `Schemata.Entity.Repository`, but no source in that project uses any type from it; its public contracts depend only on `Schemata.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`.
3. **Stale package count** — `AGENTS.md:5` and `AGENTS.md:30` say 77 `Schemata.*` packages; `find src -maxdepth 2 -name '*.csproj'` currently returns 88.
4. **Constant dictionary outside Constants** — `src/Schemata.Authorization.Foundation/Services/RegistrationMetadataMapper.cs:38` declares `private static readonly Dictionary<string, string[]> ResponseTypeRequirements` of pure string constants. Per the framework convention, static string-constant dictionaries belong in `SchemataConstants` or the component's `Constants` type (`AuthorizationConstants`).
5. **Generic/non-generic mapper split** — `src/Schemata.Mapping.Skeleton/ISimpleMapper.cs:21-84` exposes `Map<T>(object)`, `Map<T>(object, Type, Type)`, `Map<TSource,TDestination>(TSource)`, `object? Map(object, Type, Type)`, and `void Map(object, object, Type, Type)` plus the merge/mask forms. Internal callers use only the generic two-type forms and the mask/merge forms; the non-generic overloads and `Map<T>(object, Type, Type)` have no production callers in `src/`. The generic/non-generic pairs duplicate one behavior.

## Correction

Delete the two stale directories; remove the unused `Entity.Repository` reference from Tenancy.Skeleton; update the package count in `AGENTS.md` (or state that it is the count of packaged projects); move `ResponseTypeRequirements` into `AuthorizationConstants` (or a dedicated component constants type); consolidate `ISimpleMapper` to one map-to-new and one map-onto-existing surface (keep a reflection-based non-generic form only if a consumer needs it, and document that need).

### #345 — Scheduling: finalization settles execution before a second dispatcher transaction stages job state

## Evidence

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; static review, no execution.

`JobExecutionDispatcher.FinalizeAsync` first settles the claimed execution row through `ExecutionLease.TransitionAsync` / `IJobExecutionStore.TrySettleAsync` (`JobExecutionDispatcher.cs:298-302`), then sends an internal `StageJobExecutionResultRequest` through the public `IRequestDispatcher` (`JobExecutionDispatcher.cs:329`). `DefaultStageJobExecutionResultHandler` opens a separate `IRepository<SchemataJob>` unit of work and commits the job/schedule state (`Handlers/DefaultStageJobExecutionResultHandler.cs:34,59,63`).

The execution row and the job row are therefore committed in two independent transactions, and the internal transition passes through the consumer request pipeline.

## Impact

- If the staging handler fails (advisor veto, repository failure, cancellation), the execution row is already terminal but the job's `NextRunTime`/`State`/timer is not advanced. A one-time job can remain Active with a stale NextRunTime and no timer; a recurring schedule can stall.
- A consumer request advisor registered for all requests can now block or alter an internal scheduling write, coupling framework state to unrelated business pipelines.
- `FinalizeAsync` has no compensating path after the execution settle; the dispatcher loop only logs and retries a pass where the execution is no longer pending.

## Correction

Use one internal scheduling owner/transaction for execution settlement plus job-state staging (the existing stores can join one unit of work or the scheduling handler can be invoked directly with an explicit UoW), or persist a durable pending stage before settling the execution and make the stage retryable. Do not expose the state transition as a public request handler subject to arbitrary consumer advisors. Add a test where the staging write is permanently vetoed/fails after execution settlement and assert the job schedule is not lost.

### #347 — Resource/Identity: duplicate handled-response carriers in explicit execution payloads

## Evidence

HEAD `4eedbae21009b55f909e2db78600ec2e2da1e8b0`; static review, no execution.

`HandledResponse<TResult>` was introduced as the explicit advisor short-circuit output carrier (`Abstractions/Advisors/HandledResponse.cs`). `ResourceMethodExecution<TEntity,TRequest,TResponse>` (`Resource.Foundation/Advisors/ResourceMethodExecution.cs`) and `IdentityOperationExecution<TResponse>` (`Identity.Skeleton/IdentityOperationExecution.cs`) each re-declare the same `Response` member and the same "write then Handle" contract.

In `Identity.Foundation/Handlers/IdentityOperationHandler.Manage.cs`, a single operation uses both mechanisms: e.g. `execution` at line 80 (IdentityOperationExecution) and `changed` at line 102 (HandledResponse) for ChangeEmail; the pattern repeats at 31/57, 125/147, 170/198, 269/291, 314/347. `ResourceMethodOperationHandler` similarly uses `execution.Response` while standard resource handlers use `HandledResponse`.

## Impact

Two handled-output protocols now exist for one concept. A reader must know which stage uses which carrier, the pipeline runner cannot be shared, and a future change to short-circuit semantics (null check, response precedence) must be applied to each type and switch. This is parallel implementation of the same mechanism, not a distinct business payload.

## Correction

Make the execution payloads implement a shared `IHandledResponse<TResponse>` contract (or compose `HandledResponse<TResponse>`), and use one pipeline runner/handled-output helper across standard resource, custom-method, and identity stages. For identity operations, carry the response on `IdentityOperationExecution` through both advisor stages or use one explicit payload per stage consistently; do not create both in one handler.

### #349 — Tenancy: auto-inject infrastructure isolation and remove application-specific per-entity wiring

## Clarified contract (2026-10-17)

- Enabling Tenancy does **not** automatically enable every isolation surface; per-feature opt-in is expected. This is not a defect.
- Repository isolation has two modes:
  1. Dedicated database instance — no `ITenant` requirement.
  2. Shared table with an in-row tenant field — requires `ITenant`.
- **Other components must not ship `ITenant` implementations by default.** A user who needs shared-table isolation inherits the built-in entity, implements `ITenant`, and passes the custom entity type when enabling the corresponding feature (generic type replacement).
- Scheduling does **not** need adapter isolation; scheduling data is isolated by the repository only.
- The framework provides infrastructure isolation only: DI, repository, cache, cookie/session/antiforgery (and the infrastructure bridges). It does not provide application-specific isolation mechanisms.
- Accepted convergence directions: (1) automatic repository advisors, (2) automatic cache isolation, (4) explicit state scheme configuration, (5) remove the remaining hacks.

## Remaining gaps

### 1. Default `ITenant` implementations and non-generic feature entities

`SchemataActor`, `SchemataJob`, `SchemataJobExecution`, the Flow entities and `SchemataFlowEffectIntent` implement `ITenant` and carry `TenantUid`. `UseActor`/`UseFlow`/`UseScheduling` are non-generic and operate on the fixed built-in types, so users cannot substitute a tenant-aware subclass. This violates the clarified contract; tracked in #350.

### 2. Per-entity application-specific repository wiring

`TenantRepositoryBuilder.UseSharedTable<TEntity,TRepository>` requires each entity to implement `ITenant`/`IIdentifier`/`IConcurrency` and registers closed advisors per entity. `SchemataTenancyBuilder.UseRepositories` runs the consumer callback against a throwaway collection with `Guid.NewGuid()` and the `IsRegistrationProbe` bool (`TenantRepositoryBuilder.cs:50`, `SchemataTenancyBuilder.cs:36`, `TenantDatabaseExtensions.cs:55-58`). Shared-table repository isolation should instead be automatic via open-generic advisors that no-op for non-`ITenant` entities; remove `UseSharedTable`, the type harvest and the probe flag. Dedicated-database mode remains the infrastructure-only path.

### 3. Scheduling adapter isolation must be removed

`Tenancy.Foundation` contains `Scheduling/TenantJobExecutionAdapter.cs` and `Scheduling/TenantSchedulingExtensions.cs` and references `Schemata.Scheduling.Skeleton`. Per the clarified contract, Scheduling should not have adapter isolation; remove the adapter/extensions and the Scheduling dependency. Scheduling data isolation is provided by the repository (when the user supplies a tenant-aware job/execution type).

### 4. Tenancy.Foundation depends on Entity.Cache

`Tenancy.Foundation` references `Schemata.Entity.Cache` so tenant-resolution handlers can call `SuppressQueryCache` (`Handlers/FindTenantByHostHandler.cs:19-20`, `Handlers/FindTenantByIdHandler.cs:16`). A core Tenancy package should not depend on an optional component; use a generic query/cache seam or move the cache-sensitive handler into `Tenancy.Caching`.

### 5. Remaining hacks

- `TenantPolicyEvaluator` branches on `inner.GetType() == typeof(PolicyEvaluator)` (`Middlewares/TenantPolicyEvaluator.cs:23-24`).
- `TenantAntiforgeryRegistration` removes `IStatePipelineProvider` descriptors from the tenant collection (`State/TenantAntiforgeryRegistration.cs:60-68`).
- `TenantMessagingExtensions` creates a probe scope during container composition (`TenantMessagingExtensions.cs:33-39`).
- `TenantCookieRegistration.cs:59` writes `probe.log`; `MemoryCacheTenantProviderCache` uses `[ThreadStatic]` + sync wait; `SessionCache` blocks on async cache calls.

## Proposed cutover

1. Register tenant repository advisors open-generically; advisors return `Continue` for non-`ITenant` entities and use the scoped `TenantRepositoryBinding`/identity otherwise. Delete `UseSharedTable`, the type harvest, the fake tenant id and `IsRegistrationProbe`. Keep `UseDatabase` for dedicated sources.
2. Auto-wrap the host `ICacheProvider` with the tenant key normalizer when Tenancy is enabled; `UseCache` remains only for choosing a dedicated backend/outlet.
3. Remove the Scheduling adapter/extensions and the `Scheduling.Skeleton` dependency from `Tenancy.Foundation`; scheduling data isolation is repository-only.
4. Keep explicit cookie/bearer/session/antiforgery scheme configuration; no business-data isolation in state.
5. Replace concrete-type probes and descriptor removal with DI/keyed/enumerable seams; delete `probe.log` and blocking cache paths.

Related: #334, #337, #338, #342, #350.

### #350 — Actor/Flow/Scheduling: remove default ITenant entities and accept custom tenant-aware entity types

## Requirement

Other components must not implement `ITenant` by default. A user who needs shared-table tenant isolation should inherit the built-in entity, implement `ITenant`, and pass the custom entity type when enabling the corresponding feature (generic type replacement).

## Evidence at HEAD `4eedbae2`

- `Schemata.Actor.Skeleton/Entities/SchemataActor.cs:21` implements `ITenant`.
- `Schemata.Scheduling.Skeleton/Entities/SchemataJob.cs:18` and `SchemataJobExecution.cs:25` implement `ITenant`.
- `Schemata.Flow.Skeleton/Entities/SchemataProcess.cs`, `SchemataProcessCompensation.cs`, `SchemataProcessParticipant.cs`, `SchemataProcessSource.cs`, `SchemataProcessToken.cs`, `SchemataProcessTransition.cs` implement `ITenant`; `Schemata.Flow.Repository/SchemataFlowEffectIntent.cs:24` implements `ITenant`.
- `UseActor`, `UseFlow` and `UseScheduling` are non-generic features over the fixed built-in types (`Actor.Foundation/Extensions/SchemataBuilderExtensions.cs`, `Flow.Foundation/Extensions/FlowBuilderExtensions.cs`, `Scheduling.Foundation/Features/SchemataSchedulingFeature.cs`), so there is no way to substitute a tenant-aware subclass.

## Correction

- Remove `ITenant` from the default built-in entities and the tenant-specific indexes/columns.
- Make each component accept its entity types through the feature builder (e.g. `UseActor<TActor>`, `UseFlow<TProcess,TToken,...>`, `UseScheduling<TJob,TExecution>`, or an equivalent entity-type registration).
- Repository tenant advisors remain automatic and open-generic; a custom entity that implements `ITenant` is isolated solely by the repository layer.
- Scheduling has no adapter isolation; delete `TenantJobExecutionAdapter`/`TenantSchedulingExtensions` and rely on repository data isolation.

Related: #349.

### #351 — Authorization: omitted response_mode bypasses AllowedResponseModes and error callbacks lose the effective mode

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

- `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeClientAndRedirect.cs:73` rejects only an explicit `request.response_mode` outside `SchemataAuthorizationOptions.AllowedResponseModes`.
- Same advisor line 98 captures `authz.Callback = new(..., authz.Request.ResponseMode!)`. `AuthorizationCallback.ResponseMode` is declared non-null (`AuthorizeContext.cs:63`), so an omitted `response_mode` stores null.
- `AuthorizeHandler.cs:58-63` already computed the legal effective mode (`ResponseModeService.ResolveMode(...)`) as `authz.ResponseMode`; success paths use it (`AdviceAuthorizeAutoApproveSignIn.cs:129`, `AuthorizeInteractionHandler.cs:285`), while `WithCallback`/`OAuthExceptionFilter` fall back to `query` for errors.
- `AuthorizationCodeFlowFeature.cs:63` adds only `form_post` to `AllowedResponseModes`, so the default code flow advertises only `form_post` but still returns `query` when the client omits `response_mode`.

## Impact

- A client bypasses the configured response-mode allow-list by omitting `response_mode`.
- Discovery `response_modes_supported` disagrees with actual delivery.
- Implicit/hybrid error responses are delivered in `query` instead of the resolved `fragment`, contrary to OAuth 2.0 Multiple Response Types §5.

## Correction

Validate `authz.ResponseMode` (the resolved value) against `AllowedResponseModes` and capture that same value in `AuthorizationCallback`. Add tests for omitted and explicit modes on code and implicit/hybrid requests, including an error after callback capture.

## Verification boundary

Static source review only; no build/test executed for this report.

### #352 — Authorization: PKCE advisor applies to implicit and hybrid response types

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

`src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizePkce.cs:52-65` uses `options.RequirePkce` (default true) and requires a non-empty `code_challenge` without checking `response_type`. The advisor is registered for every authorize request by `AuthorizationCodeFlowFeature.cs:90`.

The same feature permits additional response types through `SchemataAuthorizationOptions.PermitResponseType`; `SchemataApplicationMetadata` and `AuthorizationSignInService` already handle `token`/`id_token`/hybrid combinations, and DCR now registers them (`AuthorizationConstants.ResponseTypeGrantTypes.Requirements`).

## Impact

When a host enables implicit or hybrid response types, a pure `token` or `id_token` authorization request is rejected for missing `code_challenge`, even though RFC 7636 defines PKCE for the authorization code flow only. The only available workaround, `RelaxPkce()`, disables PKCE for code flow as well.

## Correction

Apply the requirement only when `authz.Request.ResponseType` contains `code`. Keep the existing `plain`/`S256` grammar checks for a challenge that is actually present. Add tests for `code`, `token`, `id_token`, and hybrid combinations under `RequirePkce = true`.

## Verification boundary

Static source review only; no build/test executed for this report.

### #353 — Resource/Security: instance PERMISSION_DENIED detail bypasses IPermissionResolver

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

- Coarse authorization resolves the permission through `IPermissionResolver` (`src/Schemata.Security.Skeleton/Advisors/AuthorizationPipelineAdvisor.cs:36`), default `DefaultPermissionResolver.cs:16`: `entity.Name.Kebaberize() + "." + operation.Kebaberize()`.
- Instance authorization builds its own string in `src/Schemata.Resource.Foundation/Advisors/AuthorizeHelper.cs:109,146`: `ResourceNameDescriptor.ForType<TEntity>().Singular.Camelize() + "." + operation`.

For `OrderItem` + `Create`, the coarse check uses `order-item.create`, while the instance denial reports `orderItem.Create`. A custom `IPermissionResolver` is also ignored by the instance path.

## Impact

PERMISSION_DENIED descriptions name a permission that was never checked. Operators and clients following the error detail cannot reproduce the missing permission; custom resolvers are invisible in the resource-advisor denial path.

## Correction

Pass/inject `IPermissionResolver` into `AuthorizeHelper` and use `resolver.Resolve(context.Operation, typeof(TEntity))` in both `EnsureCoreAsync` and `FinalizeMissingAsync`. Add a test with a custom resolver and both coarse and instance denials.

## Verification boundary

Static source review only; no build/test executed for this report.

### #354 — Actor: ActorSerializingHandler is duplicated in Flow, Push and Report

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

- `src/Schemata.Flow.Actor/Handlers/ActorSerializingHandler.cs:30`
- `src/Schemata.Push.Actor/Handlers/ActorSerializingHandler.cs:26`
- `src/Schemata.Report.Actor/Handlers/ActorSerializingHandler.cs:26`

All three are `IRequestHandler<TRequest,TResult>` with the same body: capture `MessageContexts.Capture(caller)`, resolve an actor by `(system, key)`, and `AskAsync` the request. They differ only in the actor system name and key source; Report adds one definition-store lookup before routing.

## Impact

Any change to context capture, cancellation, error handling, or actor lookup must be applied three times. A missed copy silently changes one domain's serialization semantics.

## Correction

Move one generic handler into `Schemata.Actor.Foundation` and express routing through an interface implemented by scoped commands (for example `IActorScoped { string ActorSystem; string ActorKey; }`) or a `Func<TRequest, ActorId>` seam. Keep Report's definition lookup as an explicit pre-routing step or advisor. Migrate the three feature registrations to the shared handler.

## Verification boundary

Static source review only; no build/test executed for this report.

### #355 — Report: mutable ReportExecutionContext carries per-run state through a scoped service

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

`src/Schemata.Report.Foundation/Runtime/ReportExecutionContext.cs:11-15` exposes mutable `Kind`, `Operation`, and `IsCancelled` on a scoped service registered by `ServiceCollectionExtensions.cs:117`.

Writers:
- `Handlers/GenerateHandler.cs:64` sets `execution.Operation` for the inline path.
- `Jobs/ReportGenerationJob.cs:53-58` sets `Kind`, `Operation`, and `IsCancelled` in the job scope.

Reader: `Handlers/RunReportHandler.cs:41-64` consumes all three during materialization.

## Impact

Per-dispatch state is hidden from the handler signature. Two report runs sharing a scope (nested generation, a keyed dispatch, or a future parallel caller) overwrite each other's `Kind`/`Operation`/cancellation delegate; failures are not observable from the call site.

## Correction

Carry these values explicitly on `RunReportRequest` or a dedicated `ReportExecution` payload passed to `RunReportHandler`; remove the mutable scoped service. `ReportGenerationJob` already constructs the request, so it can populate the payload.

## Verification boundary

Static source review only; no build/test executed for this report.

### #356 — Resource: parent canonical derivation is duplicated with different validation

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

- `src/Schemata.Resource.Foundation/Advisors/ChildParentHelper.cs:18-28` derives a parent by splitting the canonical name and requiring at least four segments with an even count.
- `src/Schemata.Resource.Foundation/Advisors/AuthorizeHelper.cs:120-135` derives the same AIP-122 parent after validating `ResourceNameDescriptor.HasParent` and `ParseCanonicalName`, then manually joins `segments[..^2]`.

`ChildParentHelper` is used by detail/list response link advisors; `AuthorizeHelper` is used by the missing-target access stage. The two rules can disagree on malformed, odd-segment, or pattern-invalid names.

## Impact

Two owners for one canonical-name rule. A future change to nested resource naming (multi-level parents, trailing segments, wildcard parents) must be applied twice; disagreement changes either RFC 8288 links or missing-resource authorization disclosure.

## Correction

Expose one parent derivation from `ResourceNameDescriptor` (for example `ParentOf(string canonicalName)`) and route both helpers through it. Add tests with root, single-parent, multi-parent, and invalid canonical names.

## Verification boundary

Static source review only; no build/test executed for this report.

### #357 — Authorization: InteractionTokenFormat option is never read

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

`src/Schemata.Authorization.Foundation/Authentication/SchemataAuthorizationOptions.cs:41` declares `InteractionTokenFormat` with no other occurrence in `src/`, `tests/`, or docs.

`AuthorizeHandler.cs:140-150` and `EndSessionHandler.cs:188-200` create interaction and logout tokens as persisted reference rows (`ReferenceId` + `Payload`) regardless of this option. `InteractionTokenLifetime` is read, which makes the format option appear functional.

## Impact

A public configuration surface silently does nothing. A host that sets `InteractionTokenFormat` to a JWT/JWE value still gets reference tokens, with no warning.

## Correction

Remove the option, or implement the format branches and tests at both interaction-token creation sites. If the format remains fixed, document the fixed contract instead of exposing a no-op setter.

## Verification boundary

Static source review only; no build/test executed for this report.

### #358 — Modular/Core: SchemataOptionsExtensions has the same full name in two assemblies

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

- `src/Schemata.Core/Extensions/SchemataOptionsExtensions.cs:10,19` declares `namespace Schemata.Core` and `public static class SchemataOptionsExtensions`.
- `src/Schemata.Modular/Extensions/SchemataOptionsExtensions.cs:6,11` declares the same namespace and class name.
- `src/Schemata.Modular/Schemata.Modular.csproj` references `Schemata.Core`.

The extension method names differ (`GetFeatures` vs `GetModules`), so call sites may compile, but the full type name is ambiguous across the two assemblies.

## Impact

Consumers that reference both packages and name the type (for example in `typeof`, reflection, or explicit static calls) hit CS0433; inside `Schemata.Modular` the local type shadows the referenced Core type (CS0436 risk). IDE navigation and API documentation also collapse two unrelated types.

## Correction

Move the Modular class to `namespace Schemata.Modular` and a unique name (for example `ModularOptionsExtensions`). Keep `SchemataOptions` extension methods in the owning component's namespace.

## Verification boundary

Static source review only; no build/test executed for this report.

### #359 — Entity: [ResourceReference] validation is silently disabled without IResourceTypeResolver

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

- `src/Schemata.Entity.Repository/Advisors/AdviceValidateResourceReferences.cs:79-81`: when `IResourceTypeResolver` is absent, the advisor returns `AdviseResult.Continue`. Typed and polymorphic references on the entity are then not validated at all.
- `src/Schemata.Entity.Repository/Advisors/AdviceValidateResourceReferenceExistence.cs:82-86`: for a property with `ValidateExistence = true`, the same missing resolver throws `InvalidOperationException`.

Both advisors are registered unconditionally by `Entity.Repository/Extensions/ServiceCollectionExtensions.cs:41-47`. `IResourceTypeResolver` is registered by `Resource.Foundation` (`ServiceCollectionExtensions.cs:50`), so a repository-only host gets the silent-skip path unless it opts into existence validation.

## Impact

Two policies for one attribute. A typed reference can point at an unrelated or unresolvable target with no error; the existence opt-in then fails at runtime with a configuration exception. Application code cannot predict which behavior it gets from the host composition.

## Correction

Choose one policy. Prefer failing with a clear configuration error whenever an entity actually declares `[ResourceReference]` and no resolver is registered; otherwise make both advisors skip and document the repository-only contract. Add tests for typed, polymorphic, and `ValidateExistence` properties in a container without the resolver.

## Verification boundary

Static source review only; no build/test executed for this report.

### #360 — Resource: Create/Update commit path depends on idempotency execution

## HEAD static review

HEAD `c7b764ae`; no runtime execution.

## Evidence

- Unidentified dispatch: `ResourceOperationHandler.Create.cs:96-105` and `Update.cs:137-146` call `IResourceMutation.CreateAsync/UpdateAsync(entity, null)`. `ResourceMutation.ExecuteAsync` (`src/Schemata.Entity.Repository/ResourceMutation.cs:113-128`) opens its own unit of work, stages, and commits. The handler then executes `_mapper.Map<TEntity,TDetail>`.
- Identified dispatch: `ResourceOperationHandler.cs:133-160` (`ExecuteOwnedAsync`) opens the repository/unit of work, calls the mutation with the outer transaction, maps the detail, and only then commits.
- `CreateCoreAsync`/`UpdateCoreAsync` choose between those paths solely on `execution is null`, where `execution` is the idempotency payload.

## Impact

Whether a request carries `request_id` changes transaction semantics, not only replay behavior. On the unidentified path a detail-mapping failure occurs after the write has committed, so the caller receives an error for a persisted mutation and a retry can duplicate it. The identified path fails before commit.

## Correction

Use one commit-owner path for both cases: always map the detail before commit; let idempotency add reservation/settlement around that path rather than select a different one. Add a test with a mapper that throws on the detail projection and compare both request-id states.

## Verification boundary

Static source review only; no build/test executed for this report.
