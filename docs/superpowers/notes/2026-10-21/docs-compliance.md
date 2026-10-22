# Docs-compliance implementation report (Task 12 / plan Task 13)

Date: 2026-10-21. Scope: `README.md`, `AGENTS.md`, `docs/**` (excluding `docs/superpowers`, except this
report). No code, test, target-package or build-file changes; no commit. Authority: HEAD `cb7b598e`
plus working-tree facts read directly from `src/` (concurrent agents' edits are present in the working
tree; the docs describe HEAD/plan-accepted behavior as noted under Risks).

## Files changed (17)

| Group | Files | Change |
|---|---|---|
| Compliance claims | `README.md`, `docs/index.md` | Blanket AIP/OIDC/BPMN claims replaced with requirement-level wording linked to the existing matrices; Resource dependency sentence corrected against `Schemata.Resource.Foundation.csproj`. |
| Priority tables (D2) | `docs/documents/core/built-in-features.md`, `README.md`, `docs/index.md`, `docs/documents/{overview,core/overview,packages}.md`, `docs/documents/{flow,scheduling}/overview.md` | One canonical priority table at HEAD values (Transport.Http 201M in the core range, Tenancy 205M); README/index no longer duplicate it; domain tables cross-link to it; feature-dependency table regenerated from HEAD `[DependsOn]` attributes. |
| OAuth/OIDC (X-10, D3) | `docs/documents/authorization.md` | Six missing flow activations added (implicit, hybrid, none, PAR, JAR, session management), PAR endpoint row, degradation disclosure with RFC 9700 §2.1.2 / RFC 10017 §7.2 and discovery behavior; OP-session paragraph moved out of the compliance table; DCR "full metadata validation" replaced by the enumerated checks; RFC 8252 §8.4 / RFC 9068 §3 citations corrected. |
| OIDC tutorial (X-11) | `docs/cookbook/oidc-server.md`, `docs/documents/authorization.md` | `Schemata.Authorization.Identity` added to prerequisites/commands (nothing else references it); enabling example notes the package; advisor sample updated to HEAD's 5-parameter `IRepositoryAddAdvisor.AdviseAsync`; EF/DI usings completed; `scopes_supported` verification and refresh-token condition corrected to HEAD behavior. |
| AIP matrix / BPMN (X-26) | `docs/documents/resource/aip-modeling.md`, `aip-interactions.md`, `docs/documents/flow/{overview,bpmn-engine}.md`, `docs/documents/overview.md`, `README.md`, `docs/index.md` | AIP-121 Get/List MUST added; AIP-231/233/234/235 published status = Approved; AIP titles 121/126/190 restored; AIP-155 label = Supported by extension point; "full BPMN 2.0.2 engine" replaced by feature-matrix wording. |
| Stale references / AGENTS (RDS-08/09/10/11/12, D4) | `AGENTS.md`, `docs/documents/{identity,report/generation,flow/http,resource/aip-modeling,resource/aip-interactions,cookbook/actor-process-serialization}.md`, `docs/documents/core/built-in-features.md` | Fixed `Program.cs` claim, `ReportTestHost` path, `ReportExecutionContext.Operation`, `GrpcResourceNaming`, `QueryInsightResponse`, actor-handler paths, `IdentityOperationExecution.Response`; packages inventory 88 (index layer table rewritten for the full HEAD package set); flow definitions-link caveat reworded per D4 (#201 not applicable at HEAD). |

## Decisions / design

- **D2 (single table):** `docs/documents/core/built-in-features.md` is now the only priority table.
  It places `SchemataTransportHttpFeature` at **201,000,000** inside the built-in section (it must wrap
  tenancy 205M and authentication 210M) and lists `SchemataTenancyFeature` at **205,000,000** with
  `SchemataTenantPrincipalFeature` 211M / `SchemataTenantExecutionFeature` 213M. Added rows:
  Identity HTTP/SCIM/gRPC (430.1/430.1/430.2), Authorization HTTP/Identity/gRPC (460.1/460.1/460.2),
  Push HTTP/Actor (520.1/520.6), Report Actor (530.6); BPMN row links the matrix. All values
  cross-checked against the HEAD feature constants (88 `src` packages, 63 test projects, 10 targets).
- **D3 (degradation flows):** implicit/hybrid/none are documented as explicit opt-in **degradations**
  (same category as `RelaxPkce()`), with the RFC 9700 §2.1.2 / RFC 10017 §7.2 conflict stated and the
  consequence (`token`, `id_token token`, `code token`, `code id_token token` carry access tokens in
  the authorization response). The docs state the flow verifies the authorize endpoint at startup,
  that the default code-flow discovery never advertises the implicit grant, and that only installed
  flows contribute `grant_types_supported` (`implicit` for implicit/hybrid, none for `none`; response
  types appear in `response_types_supported` only when the flow is installed). This matches the
  in-progress Task 2/3 implementation read from the working tree (`AdviceDiscoveryFlowGrants`,
  `AuthorizationFlowRegistrations.RequireAuthorizeEndpoint`, flow `GrantTypes`).
- **D4:** `docs/documents/flow/http.md` now says the process→definitions link targets the unfiltered
  definitions catalog and that a specific definition cannot be located at HEAD (issue #201 not
  applicable), without adding a filter seam.
- No internal plan vocabulary (decision IDs, task numbers, X/RDS IDs) was added to published docs.

## Verification

- **DocFX render (available, used):** `docfx metadata docs/docfx.json --noRestore --disableGitFeatures`
  then `docfx build docs/docfx.json --disableGitFeatures` → metadata succeeded with **1 pre-existing
  warning** (`duplicate member WithUnitOfWork` from the API surface, not a docs file); final build
  **0 warning, 0 error**, 2,423 models, conceptual docs built. `docs/api` / `docs/_site` are gitignored.
  `--noRestore` was used to avoid colliding with the serialized build lanes.
- **Full-site markdown check** (150 committed docs + README/AGENTS): 335 tables, 0 table-structure
  problems, 0 broken relative links, 0 bad anchors.
- **Every cited `src/…`/`Schemata.*/…` path** in the changed docs validated with `git ls-files`.
- **HEAD counts:** `src/*/*.csproj` = 88, `tests/*/*.csproj` = 63, `targets/*/*.csproj` = 10.
- **Tutorial API surface** matched by static signature inspection at HEAD: `UseIdentity<TUser>()` lives
  in `Schemata.Authorization.Identity` (not referenced by any meta-package), `IRepositoryAddAdvisor`
  takes `(AdviceContext, IRepository<T>, T, MutationOptions, CancellationToken)`,
  `SchemataBuilder.ConfigureServices(Action<IServiceCollection>)`, `UseEntityFrameworkCore` signature,
  `IApplicationManager`/`IScopeManager`/`ISecurityStore` signatures, scope admission and
  `ShouldIssueRefreshToken` (`offline_access`) semantics.

## Incomplete / risks

1. **DocFX unavailable? No — render ran successfully.** No render was skipped.
2. Tutorials were verified by signature matching, not compiled: the repo has no sample projects and
   build lanes are serialized by the Lead, so no `dotnet build/test` was run.
3. The implicit/hybrid/none discovery wording depends on the concurrent Task 3 (task-9) acceptance
   ("only installed flows advertise their grants; default discovery has no implicit"). I aligned to the
   plan acceptance and to the current working-tree implementation; if that task changes the discovery
   design, re-check `authorization.md`'s degradation bullets.
4. `README.md`'s Auth/Resource sections now point at the matrices; they intentionally do not repeat
   per-row grades, so future claim changes belong in the matrix files (AGENTS now lists them as the
   canonical standards-status locations).
5. No commit was made (per Lead instruction); plan Task 13 Step 6 is deferred to the Lead. `docs/superpowers`
   was not modified except this report.

## Verification commands used

```bash
docfx metadata docs/docfx.json --noRestore --disableGitFeatures   # 0 error (1 pre-existing API warning)
docfx build docs/docfx.json --disableGitFeatures                  # 0 warning / 0 error
python3 <markdown table/link/anchor validation over committed docs>  # 0 problems (150 files / 335 tables)
git ls-files --error-unmatch <every cited source path>             # all resolve at HEAD
```
