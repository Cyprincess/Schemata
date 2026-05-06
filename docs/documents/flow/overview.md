# Flow Engine

> **Status:** Implemented as `Schemata.Flow.*`

The Flow module provides a BPMN 2.0.2 compliant process engine. It consists of a strongly-typed AST that models the process graph, a fluent DSL for building process definitions, a single-token state machine engine, and runtime services for persistence and API exposure.

## Packages

| Package | Role |
|---------|------|
| `Schemata.Flow.Skeleton` | AST types, DSL builders, runtime interfaces, entities |
| `Schemata.Flow.StateMachine` | Single-token state machine engine and validator |
| `Schemata.Flow.Foundation` | Feature registration, DI wiring, `ProcessRuntime`, controllers |
| `Schemata.Flow.Http` | REST endpoints for process interaction |
| `Schemata.Flow.Grpc` | gRPC endpoints for process interaction |

## Architecture

```
┌─────────────────────────────────────────────┐
│                ProcessBuilder (DSL)          │
│  Start().Go(Draft)                          │
│  During(Draft).Decide(                      │
│      When<Order>(o => o.Amount > 100)       │
│          .Go(Review),                       │
│      Otherwise().Go(Reject))                │
│  During(Review).Go(Approved)                │
└───────────────────┬─────────────────────────┘
                    │ produces
┌───────────────────▼─────────────────────────┐
│          ProcessDefinition (AST)             │
│  Elements: [StartEvent, NoneTask, EndEvent]  │
│  Flows:    [SequenceFlow, SequenceFlow, …]   │
└───────────────────┬─────────────────────────┘
                    │ validated by
┌───────────────────▼─────────────────────────┐
│          StateMachineValidator              │
│  ✓ Exactly one Start event                  │
│  ✓ At least one End event                   │
│  ✓ Only Exclusive/EventBased gateways       │
│  ✓ No non-interrupting boundary events       │
│  ✓ No Sub-Process / Multi-Instance          │
└───────────────────┬─────────────────────────┘
                    │ executed by
┌───────────────────▼─────────────────────────┐
│          StateMachineEngine (IFlowRuntime)   │
│  StartAsync()    → initial state             │
│  TriggerAsync()  → event-based advance       │
│  AdvanceAsync()  → auto-traverse flows       │
└───────────────────┬─────────────────────────┘
                    │ coordinated by
┌───────────────────▼─────────────────────────┐
│          ProcessRuntime (IProcessRuntime)    │
│  Persistence + engine dispatch + audit trail │
└─────────────────────────────────────────────┘
```

## Quick Start

```csharp
public class ApprovalProcess : ProcessDefinition
{
    public UserTask Draft    { get; private set; } = null!;
    public UserTask Review   { get; private set; } = null!;
    public EndEvent Approved { get; private set; } = null!;
    public EndEvent Rejected { get; private set; } = null!;

    public ApprovalProcess() {
        Start().Go(Draft);

        During(Draft).Decide(
            When<Request>(r => r.Approved).Go(Review),
            Otherwise().Go(Rejected));

        During(Review).Go(Approved);
    }
}
```

**Enable in `Program.cs`:**

```csharp
var builder = WebApplication.CreateBuilder(args)
    .UseSchemata(schema => {
        schema.UseRouting();
        schema.UseControllers();
        schema.UseFlow(flow => {
            flow.Use<ApprovalProcess>();
        });
    });
```

## Documents

- [AST Reference](ast.md) — the `FlowElement` type hierarchy
- [DSL Reference](dsl.md) — fluent builder API for defining processes
- [Engine Reference](engine.md) — `StateMachineEngine` traversal algorithm
- [Validator Reference](validator.md) — `StateMachineValidator` rules
- [Runtime Services](runtime.md) — `ProcessRuntime`, `ProcessRegistry`, HTTP/gRPC endpoints
