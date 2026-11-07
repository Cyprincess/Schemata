# Authorization Token Lifecycle

This reference collects token timing, refresh fencing, registration-token rotation, Native SSO
authority, and persisted authentication evidence. Read [Authorization](authorization.md) for feature
activation, endpoints, client policy, and the protocol requirement matrix.

The descriptions are source-traced implementation contracts, not certification or fresh execution
results. Source owners are identified beside the sections below.

## Token timestamps and refresh bounds

Issuance samples the configured `TimeProvider` once before key resolution or signing. The selected
absolute issue and expiry timestamps are passed through token creation and row persistence, so a
JWT `iat`/`exp` pair and its `SchemataToken.ExpireTime` share the same temporal authority.

The first refresh token stores its absolute chain deadline in the trusted grant context. Each
rotation applies the earlier of that deadline and `issuedAt + RefreshTokenLifetime`; key-loading or
signing latency cannot move the successor beyond either bound. A refresh request at or after the
deadline fails before redemption or successor issuance. The framework does not apply a sliding or
inactivity extension to this deadline.

## Refresh family fencing

Rotation-enabled refresh grants carry a semantic family identifier separate from resource `Name`,
authorization, session, and device identifiers. The repository token store keeps an active family
marker and commits predecessor redemption, successor rows, and marker-version rotation through one
unit of work. A concurrent rotation therefore publishes one successor set.

Refresh-token revocation and authenticated reuse invalidate the family marker and revoke its rows;
unrelated families and sessions remain valid. Access-token-only revocation affects that token alone.
Authorization-code replay, including a redemption loser, completes family invalidation and grant-wide
token revocation before `invalid_grant`. Storage failures propagate. Each repository-store operation
resolves a fresh repository; one scoped store remains usable after success, rollback, or a lost CAS.
A rotation finishing after invalidation cannot commit. Missing family state is rejected; unchanged
storage after a failed optimistic write surfaces as a configuration error. Pruning retains markers.
Native SSO device secrets share the refresh family and its fenced token batch.

Family operations report success only when every dependent write lands. Marker creation and refresh,
predecessor redemption, member publication, and member revocation each require `MutationResult.Applied`;
a repository advisor that blocks or handles one of these writes fails the family operation with
`FAILED_PRECONDITION`, and the unit of work rolls back its staged rows, so a vetoed rotation publishes
no partial successor set. Prune and participant retirement remain sweeps and report only the rows they
actually removed.

`RepositoryTokenStore.TryRedeemAsync` checks cancellation first, then admits only a supplied Valid token.
Fresh Redeemed or Revoked decisions return false without a storage write or stamp change. A stale Valid
instance still uses native timestamp compare-and-swap and loses to a concurrently consumed token.

## Registration access token rotation

RFC 7592 management rotation is fenced through `ITokenStore<TToken>.TryRotateAsync`, not through
a refresh-family batch. The store redeems the predecessor and publishes the prepared successors
in one repository unit of work; a rotation that loses the concurrency check returns `false` and
publishes no successor. A repository advisor that blocks or handles the predecessor redemption or
a successor insert fails the rotation with `FAILED_PRECONDITION`, and the unit of work rolls back
so the predecessor stays unspent and no partial successor set lands. The framework never returns a
redeemed predecessor as usable: GET and PUT respond `401 invalid_token` once the row has moved.

`RegistrationTokenLifetime` keeps its two existing forms. A null value stays non-expiring on the
initial token and on every successor the rotation publishes. A finite value supplied by the
handler row factory stamps the same expiry on each successor; the store neither invents nor caps
the lifetime.

Software statements use the host's `ISoftwareStatementValidator`. Return
`SoftwareStatementValidationResult.Invalid` for failed signature or validity checks,
`Unapproved` for a valid statement the server does not approve, or `Approved(claims)` for verified,
approved metadata. The first two map to `invalid_software_statement` and
`unapproved_software_statement`. Approved claims override the plain request before mapping and
advisors on both POST and PUT. Server-managed identifiers and credentials are excluded from that
overlay; malformed metadata types produce `invalid_client_metadata`. Without a host validator,
the server rejects statements as unapproved.

`UseDynamicClientRegistration()` installs the create (POST) and read (authorized GET) capabilities.
Replace and delete are separate installations: `UseRegistrationReplace()` adds PUT and
`UseRegistrationDelete()` adds DELETE at the configuration URI. An operation that was never
installed has no handler and no route — its HTTP verb is rejected (405 where a sibling verb shares
the route) and local dispatch finds no handler. Omitting the registration feature removes its
routes entirely (404).
Language-tagged client names and URI metadata retain their tags through storage and readback;
enabled DPoP/RAR registration advisors own their metadata, and PUT clears omitted values.

## Native SSO session authority

Native SSO records whether the grant uses online or offline session authority. An online grant has
a server-side OP-session authority slot with its own configured lifetime. The slot carries an
opaque generation minted at establishment, and the grant persists the generation it was born
under; Native exchange and Native refresh must prove that exact generation against a live slot.
Expiry replaces the slot with a fresh generation and browser sign-out removes it, so a later
authorization under the same subject and session identifier establishes a new generation and
grants born under an older one return `invalid_grant`. Remaining access or ID token rows do not
prove the online session is active.

An `offline_access` Native grant uses its refresh-family marker as the offline authority. Refresh
rotation preserves that marker; family invalidation, original offline refresh revocation, or expiry
makes Native exchange and related refresh use invalid. Browser logout clears online authority but
does not end a separate offline family. Browserless continuations use the authority persisted on the
grant and never acquire an ambient browser session identifier.

## Persisted authentication events

`AuthorizationSignInService` resolves the authentication event after session or continuation
identity is final. A new authentication calls `IAuthenticationContextProvider` once; an
authorization-code, device-code, refresh, or validated Native SSO continuation inherits the
persisted `AuthorizationGrantContext` instead. The context records the canonical subject and
subject category, effective scope/profile, grant source, optional session association, and the
original optional `acr`/`amr`/`auth_time` evidence. Missing evidence stays absent; renewal does not
synthesize `auth_time` from the issuance clock. ID-token issuance requires
`SubjectKind == GrantSubjectKinds.EndUser`, so refresh and implicit/hybrid user grants retain ID tokens
while application grants, including `client_credentials`, do not acquire them from a grant-type string.

The same context is serialized onto authorization-code, access-token, refresh-token, and Native
SSO device-secret rows. Claims advisors project that context onto access/ID token destinations;
they do not call the provider on a continuation. Introspection reads `acr` and `auth_time` from the
stored row context, so the persisted lineage remains authoritative even when a JWT claim view
would differ. `Claim.Properties` remains an in-process destination mechanism and is not persisted
as protocol context.

`SchemataToken.Key` holds a semantic slot identifier. Repository slot operations address
`(Parent, Provider, Key)` inside a serializable query-then-write transaction. Family publication,
rotation, redemption, and invalidation use the same transaction owner for their mandatory writes.
`Name` identifies the token resource without an added uniqueness check; `FindByNameAsync` queries
that resource name. Framework token indexes are ordinary. Cache-backed slot
results carry `Key` and do not acquire a resource `Name` or pass through a repository naming advisor.
Persisting a slot through the repository-backed store requires the consumer naming policy.
`SchemataTokenConflicts.IsConflict<TToken>` matches the derived token resource descriptor, including
redemption, rotation, family-invalidation, and device-polling conflict paths.

`SchemataSecurity.Key` holds a parent-scoped credential label, distinct from its resource `Name`.
The key separation does not introduce a credential-selection API or policy: `ISecurityStore`
continues to list by parent with optional kind, usage, and status filters. The repository store
orders those rows by descending creation time, then `Key`. Protocol consumers keep their existing
credential-kind and status selection rules. See [Security](security.md#stored-resource-identity).

The source paths are `src/Schemata.Security.Skeleton/Entities/SchemataToken.cs`,
`src/Schemata.Security.Skeleton/Entities/SchemataSecurity.cs`,
`src/Schemata.Security.Foundation/Stores/{RepositoryTokenStore,CacheTokenStore,SecurityStore}.cs`,
and `src/Schemata.Authorization.Foundation/Services/SecurityParents.cs`.

Timing and issuance owners: `src/Schemata.Authorization.Foundation/Services/AuthorizationSignInService.cs`,
`Services/TokenService.cs`, `Advisors/AdviceRefreshTokenValidation.cs`, and `Handlers/TokenHandler.cs`.
Registration rotation owners: `Handlers/RegisterHandler.cs` (`CurrentOrRotatedRegistrationTokenAsync`)
and `src/Schemata.Security.Foundation/Stores/RepositoryTokenStore.cs` (`TryRotateAsync`).
Native SSO authority owners: `Handlers/NativeSsoTokenExchangeHandler.cs`,
`Advisors/AdviceRefreshTokenDeviceSecret.cs`, and `Services/DefaultOpSessionService.cs` in Authorization.Foundation.

See also: [Authorization protocol matrix](authorization.md#protocol-requirement-matrix),
[Security resource identity](security.md#stored-resource-identity), and
[OIDC server recipe](../cookbook/oidc-server.md).

## Native SSO issuance

`UseNativeSingleSignOn()` routes the ID-token/device-secret exchange through
`NativeSsoTokenExchangeHandler` and the shared `AuthorizationSignInService`. The issuer applies
the configured access-token format, claims advisors, destinations, and target-client subject
projection. Reference access tokens persist a signed payload for authentication and introspection.

The handler requires trusted device-secret provenance and live online-session or offline-family authority.
Target scopes must fit registration policy and a valid target consent record, unless the configured
`IConsentModelProvider` grants implicit consent. Registered source scopes are not consent evidence;
independent target authorization may grant additional scopes. Omitted scope uses the persisted source
grant, checked against the same target policy. OAuth-only descendants cannot regain OIDC eligibility.

During refresh, `AdviceRefreshTokenDeviceSecret` searches by session and checks the secret's
application, session, resolved device, status, and expiry. Existing records with a null `Parent`
remain eligible. `TokenHandler` carries the selected secret and session in sign-in properties so
the response issuer can return the secret together with an ID token containing `ds_hash` and `sid`.
Native token exchange follows the shared ID/refresh-token grant and scope rules; it does not
rotate or echo the device secret.

## OAuth and OpenID Connect grant profiles

The authorize pipeline selects a trusted profile after PAR and Request Object normalization and
after client scope authorization. An authorized `openid` scope selects OpenID Connect; other
authorized scopes select OAuth. The profile is stored with the grant context and inherited by
authorization-code, refresh, device, and Native SSO continuations.

OAuth grants retain authorization code, PKCE, state, access and refresh tokens, DPoP, PAR, Request
Objects, rich authorization details, resource indicators, interaction parameters, authentication
evidence, and the configured `offline_access` convention. The Profile endpoint also accepts these
grants when the persisted subject category is an end user. Application grants, including
`client_credentials`, cannot use Profile as an end-user identity source.

ID Tokens, `session_state`, relying-party logout participation, and Native SSO device secrets are
created only for an OpenID Connect grant. An OAuth authorization request that supplies `id_token`,
top-level `nonce`, `claims.id_token`, `id_token_hint`, or `device_sso` is rejected as an incompatible
profile request. DPoP nonces are independent proof values carried in DPoP protocol messages.

`session_state` follows the OpenID Connect Session Management contract with a salt-only
interaction carrier. The authorize advisor mints a random salt per request, overwrites the
server-side `AuthorizeRequest.SessionStateSalt` with it, and carries it through the interaction
payload into the sign-in properties, so a client-supplied value never survives. The initial
value binds SHA-256 over `client_id`, the redirect origin, the current OP user-agent state
cookie value, and the salt, and serves same-request error redirects. The successful callback
recomputes it after session issuance from the live OP user-agent state; a sign-in that rotated
that state publishes the post-login value, while only the salt ever crosses the interaction.

Scope narrowing creates a descendant grant with its own ceiling. Dropping `openid` converts that
descendant to OAuth, and a later refresh cannot restore either the scope or OpenID Connect profile.
The separately issued parent credential remains unchanged. Discovery continues to describe the
features installed on the server rather than one grant's profile.

Grant-profile sources: `src/Schemata.Authorization.Foundation/Advisors/AdviceAuthorizeGrantProfile.cs`,
`Services/AuthorizationGrantContexts.cs`, `Services/AuthorizationSignInService.cs`,
`Advisors/AdviceAuthorizeSessionState.cs`, and `Services/SessionStateFormulator.cs`.
