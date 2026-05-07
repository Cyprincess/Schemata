# Workflow

This guide adds an enrollment state machine to the Student entity. After completing it, students will transition through Draft, Enrolled, and Graduated states, with every transition recorded as an auditable `SchemataFlowTransition` row.

## Add the workflow package

`Schemata.Application.Complex.Targets` already includes `Schemata.Workflow.Foundation`. If you are composing packages manually:

```shell
dotnet add package --prerelease Schemata.Workflow.Foundation
```

## Make Student stateful

Add `IStateful` to the Student entity. This interface requires a single `State` property:

```csharp
using Schemata.Abstractions.Entities;

public class Student : IIdentifier, ICanonicalName, ITimestamp, ISoftDelete, IStateful
{
    // ... existing properties ...

    // IStateful
    public string? State { get; set; }
}
```

The workflow system also uses `IStatefulEntity`, which combines `IIdentifier`, `IStateful`, and `ITimestamp` -- all of which Student already implements. This means Student satisfies the `IStatefulEntity` constraint required by `StateMachineBase<TI>`.

## Enable the workflow subsystem

Add `UseWorkflow()` to the Schemata builder in `Program.cs` and register the state machine:

```csharp
schema.UseWorkflow()
      .Use<EnrollmentStateMachine, Student>();
```

`UseWorkflow()` registers the default `SchemataWorkflow` and `SchemataFlowTransition` entity types, the `SchemataWorkflowManager`, the workflow controller, and the response mapping. The `.Use<TStateMachine, TI>()` call registers the state machine as `StateMachineBase<Student>` in DI.

## Add workflow tables to the DbContext

Register the workflow entities so EF Core creates the backing tables:

```csharp
using Microsoft.EntityFrameworkCore;
using Schemata.Workflow.Skeleton.Entities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Student>            Students    => Set<Student>();
    public DbSet<SchemataWorkflow>   Workflows   => Set<SchemataWorkflow>();
    public DbSet<SchemataFlowTransition> Transitions => Set<SchemataFlowTransition>();

    protected override void ConfigureConventions(
        ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.UseTableKeyConventions();
    }
}
```

## Define the state machine

Create `EnrollmentStateMachine.cs`. Subclass `StateMachineBase<Student>` and define states, events, and transitions using the Automatonymous DSL:

```csharp
using Automatonymous;
using Schemata.Workflow.Skeleton;

public class EnrollmentStateMachine : StateMachineBase<Student>
{
    public EnrollmentStateMachine()
    {
        InstanceState(x => x.State);

        Event(() => Enroll);
        Event(() => Graduate);

        Initially(
            When(Enroll)
                .TransitionTo(Enrolled));

        During(Enrolled,
            When(Graduate)
                .TransitionTo(Graduated));

        SetCompletedWhenFinalized();
    }

    public State Enrolled  { get; private set; } = null!;
    public State Graduated { get; private set; } = null!;

    public Event Enroll   { get; private set; } = null!;
    public Event Graduate { get; private set; } = null!;
}
```

Key points:

- `InstanceState(x => x.State)` tells Automatonymous which property holds the current state string.
- `Initially(...)` defines transitions from the initial (null/empty) state.
- `During(state, ...)` defines transitions allowed from a specific state.
- New entities start in the `Initial` state. After `Enroll` they move to `Enrolled`, and after `Graduate` they move to `Graduated`.

## Workflow API endpoints

The `WorkflowController` is registered automatically and is decorated with `[Authorize]`, so all endpoints require authentication by default. It exposes three operations:

| Method | Path             | Description                                                          |
| ------ | ---------------- | -------------------------------------------------------------------- |
| `GET`  | `/Workflow/{id}` | Get a workflow by ID, including current state and available events   |
| `POST` | `/Workflow`      | Submit a new workflow for a stateful entity                          |
| `POST` | `/Workflow/{id}` | Raise an event on an existing workflow to trigger a state transition |

Each endpoint runs through its own advisor pipeline (`IWorkflowGetAdvisor`, `IWorkflowSubmitAdvisor`, `IWorkflowRaiseAdvisor`). To add authorization checks, chain `.WithAuthorization()`:

```csharp
schema.UseWorkflow()
      .Use<EnrollmentStateMachine, Student>()
      .WithAuthorization();
```

## Trigger transitions

Submit a workflow for a student, then raise events to drive state changes. Every transition is recorded as a `SchemataFlowTransition` row with `Previous`, `Posterior`, `Event`, and audit metadata.

```shell
# Submit a workflow for student with ID 1
curl -X POST http://localhost:5000/Workflow \
     -H "Content-Type: application/json" \
     -d '{"type":"Student","instance":{"id":1}}'

# Raise the Enroll event (use the workflow ID from the response)
curl -X POST http://localhost:5000/Workflow/1 \
     -H "Content-Type: application/json" \
     -d '{"event":"Enroll"}'

# Raise the Graduate event
curl -X POST http://localhost:5000/Workflow/1 \
     -H "Content-Type: application/json" \
     -d '{"event":"Graduate"}'
```

The Get endpoint returns the current state, the full state machine graph, and the list of events available from the current state:

```shell
# Get workflow with current state and available transitions
curl http://localhost:5000/Workflow/1
```

## Verify

```shell
dotnet run
```

1. Create a student via `POST /students`.
2. Submit a workflow via `POST /Workflow` with the student's type and ID.
3. Call `GET /Workflow/{id}` -- the state should be `Initial` and the available events should include `Enroll`.
4. Raise `Enroll` via `POST /Workflow/{id}` -- the state should change to `Enrolled`.
5. Raise `Graduate` -- the state should change to `Graduated` with no further events available.

## Next steps

- [Module System](module-system.md) -- extract the Student feature into a self-contained module
- For the full API surface and architecture details, see [Workflow](../documents/workflow.md)
