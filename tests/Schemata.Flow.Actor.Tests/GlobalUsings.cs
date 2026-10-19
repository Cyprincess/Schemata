global using Schemata.Flow.Skeleton.Entities;
global using StateMachineEngine = Schemata.Flow.StateMachine.StateMachineEngine<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition>;
global using FlowRunner = Schemata.Flow.Foundation.FlowRunner<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken>;
global using IFlowRunner = Schemata.Flow.Foundation.IFlowRunner<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken>;
