# Wave 2 status (owner "继续" round)

Remaining plan work is being implemented in parallel. Shared tasks:

| Task | Plan | Owner | Scope |
|---|---|---|---|
| task-9 | Tasks 2–3 | security-reviewer (auth-flows) | Authorization response_mode delivery + implicit/hybrid/none metadata |
| task-10 | Task 4 | data-reviewer (resource-commit) | Resource single commit owner for delete/undelete |
| task-11 | Task 7 | execution-reviewer (scheduling-settle) | Scheduling settlement + persisted background context |
| task-12 | Task 13 | docs-reviewer (docs-compliance) | Docs compliance claims and priority/package tables |
| task-13 | Task 14 | nullable-sweep-b (flow-wire) | Flow wire requests / dispatch commands unification |

Already landed in this wave: Task 1 commit `cb7b598e` (implicit ID token). Remaining unassigned plan work after this wave:
Task 5 (tenant cache key frame), Task 6 (single link contributor seam), Task 9 (transport/authorization boundaries), Task 12 (dead surface/hygiene).
