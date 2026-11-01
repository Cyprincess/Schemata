global using Schemata.Flow.Skeleton.Entities;
global using BpmnEngine = Schemata.Flow.Bpmn.BpmnEngine<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition>;
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
global using ProcessPersistence = Schemata.Flow.Foundation.ProcessPersistence<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition,
    Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation,
    Schemata.Flow.Skeleton.Entities.SchemataProcessSource>;
global using FlowAccessPolicy = Schemata.Flow.Foundation.FlowAccessPolicy<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessParticipant>;
global using FlowParticipantManager = Schemata.Flow.Foundation.FlowParticipantManager<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessParticipant>;
global using SchemataFlowBuilder = Schemata.Flow.Foundation.Builders.SchemataFlowBuilder<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition,
    Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation,
    Schemata.Flow.Skeleton.Entities.SchemataProcessParticipant,
    Schemata.Flow.Skeleton.Entities.SchemataProcessSource,
    Schemata.Flow.Skeleton.Entities.SchemataFlowEffectIntent>;
