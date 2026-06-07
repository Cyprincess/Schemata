using Schemata.Abstractions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.StateMachine;

public sealed class StateMachineFlowEngineValidator : IFlowEngineValidator
{
    #region IFlowEngineValidator Members

    public string EngineName => SchemataConstants.FlowEngines.StateMachine;

    public void Validate(ProcessDefinition definition) { StateMachineValidator.Validate(definition); }

    #endregion
}
