using Schemata.Abstractions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.StateMachine;

/// <summary>Validates process definitions for the built-in state machine engine.</summary>
public sealed class StateMachineFlowEngineValidator : IFlowEngineValidator
{
    #region IFlowEngineValidator Members

    public string EngineName => SchemataConstants.FlowEngines.StateMachine;

    public void Validate(ProcessDefinition definition) { StateMachineValidator.Validate(definition); }

    #endregion
}
