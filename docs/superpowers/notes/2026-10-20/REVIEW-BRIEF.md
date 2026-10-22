# Review brief — Schemata HEAD 90ff2a5f (2026-10-20 round)

This file is the shared instruction sheet for the review team. Read it fully before starting.

## Context

- Repository: `/Users/chino/Developer/Cyprin/Schemata` (shared workspace, same filesystem for all members).
- Baseline commit of the review window: `00c34ae07` ("test: tests rework").
- HEAD: `90ff2a5f` ("chore: remove dead resource and insight surfaces"). The CI build for HEAD is red; the Lead is fixing it in the working tree right now.
- Review scope: **all 93 commits in `00c34ae07..HEAD` plus the whole project state at HEAD**. The 28 commits dated 2026-10-19 are the direct execution of `docs/superpowers/plans/2026-10-19-post-refactor-remediation.md`; they must be verified as a first-class part of the review (did the executed tasks actually fix the issue, or only move the symptom?).
- Prior context (do not trust blindly, verify against code): `docs/superpowers/specs/*.md`, `docs/superpowers/plans/*.md`, `AGENTS.md`, `docs/superpowers/AIP.txt`, `docs/superpowers/OIDC.txt`.
- Open-issue ledger snapshot: `docs/superpowers/notes/2026-10-20/open-issues.md`.
- Uncommitted working-tree changes at the time of writing are the Lead's CI fix (analyzer release tracking, XML doc comments in `Schemata.Resource.Foundation/Advisors`, xUnit analyzer assertions in `Schemata.Tenancy.Integration.Tests`, and `Schemata.Flow.Repository` doc crefs). **Treat those as noise**: review the committed HEAD state; where the working tree differs from HEAD, review HEAD's committed content (`git show HEAD:<path>`).

## What the Lead needs from you

A single findings file: `docs/superpowers/notes/2026-10-20/<your-name>.md` (only file you may write; do not edit source or tests).

It must contain, in this order:

1. **Issue verdicts** — for each issue number assigned to you below:
   `#NNN | Fixed / Partially fixed / Still open / Obsolete / Duplicate | evidence (file:line or commit) | one-sentence rationale`.
   "Fixed" requires code evidence that satisfies the issue's stated acceptance, not merely a related commit. If the issue body is stale relative to the current design, say so and propose the updated wording.
2. **Findings** — each finding with an ID `R<your-initials>-NN`, in this table shape:

   | ID | Type | Sev | Area | Evidence (file:line + snippet) | Principle violated | Proposed fix (reuse which existing mechanism) | Related issue |
   |----|------|-----|------|-------------------------------|--------------------|------------------------------------------------|----------------|

   `Type`: `D` defect / `C` duplicate-or-split implementation / `P` policy violation / `K` capability gap / `O` over-design, over-abstraction or over-defense / `X` stale, dead or unused surface.
   `Sev`: `P0` (breaks correctness/security), `P1` (framework consistency or real user-visible gap), `P2` (maintainability/duplication), `P3` (hygiene).
   Every finding must cite a concrete `file:line` and quote the relevant code (<= 5 lines). No finding without evidence. Consolidate findings that share one root cause; prefer 10 strong findings over 60 style notes.
3. **Intentional non-issues** — things you checked and deliberately did NOT flag, with the reason (e.g. allowed downgrade options).
4. **Top-10** — the ten findings that should be acted on first, in order.

## Review principles to enforce (from the project owner, verbatim intent)

1. `[ResourceReference]` (ref attribute) designates a **string field as an association to the target type's canonical name**; it supports a single target type or a complex/polymorphic target resolved by canonical name. Canonical-name association is the framework policy — flag any reference implementation that uses ids/instances/other keys or diverges from this.
2. `RelaxXXX`-style configuration methods in Authorization are **allowed framework-user downgrade options** — do not report them as "second capability switches".
3. Do not create a new implementation/mechanism to solve one problem when an existing mechanism/implementation can be integrated or reused. If one technical requirement has several implementations or paths, consolidate them; this is a framework/platform and must stay internally consistent.
4. New packages must be justified by a real responsibility boundary; consider merging packages. Trace the project-reference graph and check bridge/provider packages.
5. **No state-bit/state-flag switches controlling features.** Optional capabilities must be expressed through ASP.NET Core DI and the advisor mechanisms. No `bool`/flag replacing DI `GetService`/`GetRequiredService`/`GetServices` runtime resolution, and no flag-based branching between multiple optional implementations (use optional-service resolution, keyed resolution, or enumerable resolution).
6. **Strict component isolation**: Tenancy component logic must not intrude into other components; other components likewise must not couple to each other. `Report -> Insight` is a design-stage exception. Bridge packages (component A obtains capability from component B) and provider packages (different backend implementations of one component) must fully use DI and advisors.
7. Service resolution, optional-feature invocation and optional-feature detection (especially runtime differences) must use DI `GetService`/`GetRequiredService`/`GetServices`; multiple implementations working together must use enumerable `GetServices`.
8. No state flags in advisors or advice contexts; no wrap-style data passing. Per-request data belongs in explicit payloads or DI services.
9. Avoid generic/non-generic type or method splits, and avoid same-function different-path duplication/splits.
10. Avoid testing for testing's sake; avoid over-defense (redundant guards, sentinel values, defensive copies when DI guarantees the invariant).
11. New types/files: analyze the namespace/directory. Pure static types for string-constant dictionaries belong in `SchemataConstants` (`Schemata.Abstractions`) or the component's own `Constants` type.
12. No static classes/static values for runtime state passing; especially per-request state (user/permission/session). Such state passing is unreliable and inelegant.
13. Docs must not overstate or deviate: verify definitions/terms against the actual standards (web research allowed and expected for AIP/OIDC/SCIM/RFC claims) and flag over-design/over-abstraction/over-defense.

Do not report: naming/style nits, XML doc wording, missing tests as such, or design choices explicitly recorded as settled in `AGENTS.md` (three-phase lifecycle, wrap order, AIP claim-level classification, explicit payloads, tenant isolation boundaries).

## Method

- Static reading only. **Do not run `dotnet build`, `dotnet test`, or any other build/test command** — the Lead and CI verification own the build lanes; concurrent builds corrupt `artifacts/`.
- Use `git log/show/diff` for history; `grep`/`read` for current state. Use `web_search`/`web_fetch` only when a standard/claim needs external verification.
- Start with `AGENTS.md`, then the executed 2026-10-19 plan, then your assigned source tree. Use `git diff 00c34ae07..HEAD -- <path>` to see the full development of a package.
- Be adversarial about the *recent* work: the previous round claimed to fix issues; check whether the fix is real, complete, and single-owner. Also check whether the fix introduced a second path, a state flag, a static carrier, or a public-surface split.

## Assignments

- `security-reviewer`: `src/Schemata.{Abstractions,Advice,Common,Core,Modular,Security.*,Authorization.*,Identity.*,Scim}`; verify issues #351, #352, #353, #354, #357, #358, #359, #343, #107, #102, #101, #100, #118, #114, #109, #108, #124, #125, #126, #127, #128, #115, #112, #110. Focus: OAuth2/OIDC/RFC 6749/6750/7636/8414/9126/9700/7662/7009/9068/9470/9396 correctness where the code claims it, security boundary, single owner for session/token/claims, DI/advisor-based optional flows (implicit/hybrid/none/CIBA), no per-request state in static carriers.
- `data-reviewer`: `src/Schemata.{Entity.*,Resource.*,Tenancy.*,Caching.*,Mapping.*,Validation.*,Transport.*,Domain.Skeleton}`; verify issues #334, #336, #337, #338, #342, #343, #345, #347, #349, #350, #356, #359, #360, #196, #197, #198, #200, #201, #202, #203, #204, #205, #61. Focus: `[ResourceReference]` canonical-name policy, repository/UoW/idempotency ownership, tenancy isolation, caching providers, mapping providers, transport seams, RFC 8288 link projection across HTTP/gRPC, AIP resource contracts affected by the explicit-payload refactor.
- `execution-reviewer`: `src/Schemata.{Messaging.*,Event.*,Actor.*,Flow.*,Scheduling.*,Report.*,Insight.*,Push.*,Expressions.*}`; verify issues #285, #291, #345, #354, #355, #196, #198, #200, #201, #204, #205, #61. Focus: dispatcher wraps and advisor pipeline, actor routing/ownership, durable execution settlement, flow engines/BPMN, report execution, insight federated query, push delivery, expression semantics; check bridge packages use DI/enumerable advisors and no duplicated hand-rolled routing.
- `docs-reviewer`: `docs/**`, `README.md`, `AGENTS.md`, `targets/**`. Verify the documentation's definitions and claims against the standards named in `docs/superpowers/AIP.txt` and `docs/superpowers/OIDC.txt` (fetch the actual pages/RFCs; never infer from the number or title). Check especially: compliance claims (Enforced/Supported/Partial tables) that overstate capability; OIDC/OAuth response mode, PKCE, DCR, introspection/revocation, CIBA claims; SCIM RFC 7643/7644 claims; RFC 8288 link claims; AIP claim matrices; tutorial code that no longer matches the API surface; `AGENTS.md` counts/commands/directory tables that are wrong. Flag over-design/over-abstraction/over-defense and terminology deviation. Also check `targets/` meta-package feature selection (`Use*` flags) against the "no state-flag feature control / prefer DI+advisor" principle.

If you finish early, deepen the area with the highest-severity findings rather than widening with low-value nits. Report progress to the Lead only when you have the findings file written; the Lead will read the file.
