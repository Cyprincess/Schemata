global using Schemata.Flow.Skeleton.Entities;
global using FlowPersistenceScope = Schemata.Flow.Foundation.FlowPersistenceScope<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition,
    Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation,
    Schemata.Flow.Skeleton.Entities.SchemataProcessSource>;
global using FlowRunner = Schemata.Flow.Foundation.FlowRunner<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken>;
global using StateMachineEngine = Schemata.Flow.StateMachine.StateMachineEngine<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition>;
global using IFlowRuntime = Schemata.Flow.Skeleton.Runtime.IFlowRuntime<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition>;
global using IFlowRunner = Schemata.Flow.Foundation.IFlowRunner<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken>;
global using ProcessPersistence = Schemata.Flow.Foundation.ProcessPersistence<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition,
    Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation,
    Schemata.Flow.Skeleton.Entities.SchemataProcessSource>;
global using RepositoryFlowEffectRecorder = Schemata.Flow.Foundation.RepositoryFlowEffectRecorder<
    Schemata.Flow.Skeleton.Entities.SchemataFlowEffectIntent>;
global using SchemataFlowFeature = Schemata.Flow.Foundation.Features.SchemataFlowFeature<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition,
    Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation,
    Schemata.Flow.Skeleton.Entities.SchemataProcessParticipant,
    Schemata.Flow.Skeleton.Entities.SchemataProcessSource,
    Schemata.Flow.Skeleton.Entities.SchemataFlowEffectIntent>;
global using SchemataFlowStateMachineFeature = Schemata.Flow.StateMachine.Features.SchemataFlowStateMachineFeature<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition>;
global using SchemataFlowBuilder = Schemata.Flow.Foundation.Builders.SchemataFlowBuilder<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition,
    Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation,
    Schemata.Flow.Skeleton.Entities.SchemataProcessParticipant,
    Schemata.Flow.Skeleton.Entities.SchemataProcessSource,
    Schemata.Flow.Skeleton.Entities.SchemataFlowEffectIntent>;
