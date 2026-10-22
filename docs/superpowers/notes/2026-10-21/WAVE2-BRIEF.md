# Wave 2 implementation brief — post-review remediation plan Tasks 1–14

The owner asked to continue the remaining planned remediation. Work through your assigned plan task(s) in
`docs/superpowers/plans/2026-10-20-post-review-remediation-plan.md`, using the requirements, interfaces,
steps and acceptance there as the contract. Read the matching spec sections and decisions first
(`docs/superpowers/specs/2026-10-20-post-review-remediation-spec.md`, §12 decisions D1–D10).

## Rules

1. Edit only files inside your assigned scope. Never edit another task's files, `docs/superpowers` (other than
   your report), or `docs/` unless your task explicitly includes docs. Do not commit; the Lead commits.
2. Use `read`/`edit`/`write` for changes. Prefer the plan's stated files; explain any deviation in the report.
3. Builds/tests are serialized. Use `bash /tmp/dsh-build.sh dotnet build <project> -c Release /p:TreatWarningsAsErrors=true /p:NuGetAudit=false`
   (workspace root) and the same wrapper for `dotnet test ... -c Release -f net10.0 --no-build` (or net8.0). Never run
   bare `dotnet build/test` concurrently with another agent.
4. Preserve behavior/API unless the plan explicitly changes it. Follow AGENTS.md style: no forced null-forgiving
   access, explicit usings, no hand-written ConfigureAwait, warnings are errors, `TryAddEnumerable` for advisors,
   no static per-request state.
5. Add the tests the plan requires. If a test cannot be made deterministic, say so; do not weaken assertions to pass.
6. If you are blocked by a design decision, missing package, or another task's scope, stop and report it; do not invent
   a second implementation path.
7. Run the plan's targeted test project(s) for both TFMs you can, and report exact pass/fail counts.

## Report

Write `docs/superpowers/notes/2026-10-21/<your-name>.md` with: files changed, design/decisions, tests added and results,
anything incomplete/risky. Then send the Lead a <=10 line summary and mark your shared task complete.
