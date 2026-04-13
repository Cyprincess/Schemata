# Workflow

Schemata provides a workflow engine that tracks stateful entities through state machine transitions, with an API controller, advisor pipelines, and access control integration.

## Packages

| Package                        | Role                                                            |
| ------------------------------ | --------------------------------------------------------------- |
| `Schemata.Workflow.Skeleton`   | Entities, state machine base, managers, models, access provider |
| `Schemata.Workflow.Foundation` | Feature, controller, builder, advisors                          |

## Entity types

### SchemataWorkflow

Represents a workflow instance. Table: `SchemataWorkflows`. Canonical name: `workflows/{workflow}`.

- `InstanceId` -- the primary key of the stateful entity this workflow tracks
- `InstanceType` -- the fully qualified CLR type name of the stateful entity

### SchemataFlowTransition

Records a single state transition within a workflow. Table: `SchemataTransitions`. Canonical name: `workflows/{workflow}/transitions/{transition}`.

- `WorkflowId` -- foreign key to the parent workflow
- `WorkflowName` -- display name of the parent workflow
- `Previous` -- the state before the transition
- `Posterior` -- the state after the transition
- `Event` -- the event name that triggered the transition (from `ITransition`)
- `Note` -- optional note (from `ITransition`)
- `UpdatedById`, `UpdatedBy` -- audit fields identifying who triggered the transition

### IStatefulEntity

A composite interface for entities that participate in workflows:

```csharp
public interface IStatefulEntity : IIdentifier, IStateful, ITimestamp;
```

Where `IStateful` provides:

```csharp
public interface IStateful
{
    string? State { get; set; }
}
```

## StateMachineBase\<TI\>

Abstract base class for defining state machines. Extends `AutomatonymousStateMachine<TI>` from the Automatonymous library:

```csharp
public abstract class StateMachineBase<TI> : AutomatonymousStateMachine<TI>, IDisposable
    where TI : class, IStatefulEntity
```

Subclass this to define states, events, and transition rules using the Automatonymous DSL. Key methods:

- `GetEvent<T>(name)` -- retrieves a named event that carries data of type `T`
- `GetCurrentState(instance)` -- returns the current `State<TI>` for the entity

Supports optional `StateObserver<TI>` and `EventObserver<TI>` for monitoring state changes and event raises.

## IWorkflowManager

The non-generic workflow manager interface provides type-erased access:

- `GetInstanceTypeAsync(type, ct)` -- resolves the CLR type from a type name string
- `FindAsync(id, ct)` -- finds a workflow by ID
- `FindInstanceAsync(id, ct)` -- finds the stateful entity for a workflow
- `GetInstanceAsync(workflow, ct)` -- gets the entity linked to a workflow
- `ListTransitionsAsync(id, ct)` -- lists all transitions as `IAsyncEnumerable<SchemataFlowTransition>`
- `CreateAsync(instance, principal, ct)` -- creates a new workflow for an entity
- `CreateAsync(instance, id, ct)` -- creates a workflow record for an existing entity by type and ID
- `RaiseAsync<TEvent>(workflow, event, principal, ct)` -- raises an event causing a state transition
- `MapAsync(workflow, options, principal, ct)` -- maps a workflow to a response object

### IWorkflowManager\<TWorkflow, TTransition, TResponse\>

Strongly-typed variant with the same operations but using concrete types.

### SchemataWorkflowManager\<TWorkflow, TTransition, TResponse\>

The default implementation. Coordinates:

- `IRepository<TWorkflow>` and `IRepository<TTransition>` for persistence
- `StateMachineBase<TI>` resolved from DI for the entity's type
- `ITypeResolver` for resolving CLR types from string names
- `ISimpleMapper` for mapping `WorkflowDetails` to response DTOs

The `RaiseAsync` method resolves the correct state machine for the workflow's instance type via reflection and invokes `StateMachineBaseExtensions.RaiseEventAsync`.

The `MapAsync` method retrieves the state machine graph, available next events, and transition history, then maps everything into a response object via the configured mapping.

## SchemataWorkflowOptions

Configuration specifying the concrete types:

- `WorkflowType` -- the `SchemataWorkflow` subclass
- `TransitionType` -- the `SchemataFlowTransition` subclass
- `WorkflowResponseType` -- the response DTO type
- `TransitionResponseType` -- defaults to `TransitionResponse`
- `Package` -- optional scoping identifier

## UseWorkflow()

```csharp
builder.UseWorkflow(
    configure: options => { ... },
    mapping:   map => { ... }
);
```

### Overloads

- `UseWorkflow()` -- uses `SchemataWorkflow`, `SchemataFlowTransition`, `WorkflowResponse`
- `UseWorkflow<TWorkflow, TTransition>()` -- custom workflow/transition, default response
- `UseWorkflow<TWorkflow, TTransition, TResponse>()` -- fully custom types

Returns a `SchemataWorkflowBuilder` for further configuration.

### Default mapping

The default mapping configures the following projections from `WorkflowDetails` to the response:

- `Graph` from the state machine's `StateMachineGraph`
- `Events` -- the list of available next events
- `Transitions` -- the transition history
- `Id` from the workflow's ID
- `State` from the entity instance's current state
- `CreateTime`, `UpdateTime` from the workflow

### Feature behavior

`SchemataWorkflowFeature` depends on `SchemataControllersFeature` and `SchemataSecurityFeature`. It registers:

- `ITypeResolver` as `TypeResolver` (singleton)
- `SchemataWorkflowManager<TWorkflow, TTransition, TResponse>` (scoped)
- Both `IWorkflowManager<TWorkflow, TTransition, TResponse>` and `IWorkflowManager` (transient, forwarding)
- A default `Vertex` to `string` mapping using the vertex title
- The configured workflow response mapping

## SchemataWorkflowBuilder

Fluent builder returned by `UseWorkflow()`:

### WithAuthorization()

Registers the built-in authorization advisors for workflow operations:

```csharp
builder.UseWorkflow()
       .WithAuthorization();
```

This registers `AdviceWorkflowGetAuthorize`, `AdviceWorkflowSubmitAuthorize`, and `AdviceWorkflowRaiseAuthorize` as scoped advisor implementations.

### Use\<TStateMachine, TI\>()

Registers a state machine for a stateful entity type:

```csharp
builder.UseWorkflow()
       .Use<OrderStateMachine, Order>();
```

Registers `StateMachineBase<TI>` as `TStateMachine` (scoped).

## WorkflowController

Mounted at `~/Workflow`. All endpoints require `[Authorize]`.

### GET ~/Workflow/{id}

Retrieves a workflow. Runs the `IWorkflowGetAdvisor` pipeline. Returns the mapped workflow response or 404.

### POST ~/Workflow

Submits a new workflow. Runs the `IWorkflowSubmitAdvisor` pipeline. The request contains the entity type name and instance data. The controller maps the request into an `IStatefulEntity`, creates the workflow via the manager, and returns the mapped response.

### POST ~/Workflow/{id}

Raises an event on an existing workflow. Runs both `IWorkflowGetAdvisor` and `IWorkflowRaiseAdvisor` pipelines. Delegates to `IWorkflowManager.RaiseAsync` to trigger the state machine transition, then returns the updated workflow response.

## Advisor interfaces

- `IWorkflowGetAdvisor` -- runs before reading a workflow
- `IWorkflowSubmitAdvisor` -- runs before creating a workflow
- `IWorkflowRaiseAdvisor` -- runs before raising an event on a workflow

## Access control

See the [Security](security.md) documentation for `WorkflowAccessProvider<T, TRequest>`, which checks for claims in the format `workflow-{operation}-{entity}` with wildcard support.
