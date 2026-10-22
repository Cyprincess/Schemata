# Implementation brief — 2026-10-21 (owner-requested completion round)

Repo: /Users/chino/Developer/Cyprin/Schemata, branch master, HEAD `70d7aafb`.
You are a code implementer on a shared workspace. The Lead owns builds, commits, gitea/GitHub and final verification.
Read this file fully, then your task description.

## Hard rules

1. Edit code directly with the read/edit/write tools. Write only inside your assigned scope; never touch files outside it, tests, docs, or `docs/superpowers/**` (except your own report file).
2. **Do NOT run `dotnet build`, `dotnet test`, or any build/package command.** The Lead serializes all builds; concurrent builds corrupt `artifacts/`. You may use `grep`/`rg`/`read`/`git diff` freely.
3. Preserve behavior. These are structural/cleanup changes, not redesigns. Do not change public API shapes unless the task explicitly says so.
4. Keep the repo style: explicit usings (`ImplicitUsings=false`), nullable enabled, `ConfigureAwait(false)` is woven by Fody (never write it), XML doc comments on public members, no unused usings you introduced.
5. Nullable rule (repo convention): never force a nullable value with `foo!.Bar`. Use pattern matching + early return:
   - `if (foo is null) { throw ...; }` then use `foo` (single evaluation);
   - `if (foo is not { Bar: true } f) { throw ...; }` then use `f`;
   - `if (foo is not Bar b) { throw ...; }` then use `b`;
   - `if (foo?.Bar != true) { throw ...; }` / `if (foo?.S?.IsSubsetOf(bar) != true)`.
   Prefer `switch` expressions to nested if/else. Do not add state flags/static per-request state.
6. If a change needs a design decision or risks a public API change, stop and report it instead of guessing.
7. Write a report to `docs/superpowers/notes/2026-10-21/<your-name>.md`: changed files, what changed, deleted files, any compile-risk or deliberate exceptions, and anything you could not complete.
   Then send the Lead a <=10 line message with the report path, number of files changed, and top risks.
