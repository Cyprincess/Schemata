using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn;

/// <summary>Validates process definitions for the BPMN engine.</summary>
public sealed class BpmnFlowEngineValidator : IFlowEngineValidator
{
    #region IFlowEngineValidator Members

    public string EngineName => FlowConstants.Engines.Bpmn;

    public void Validate(ProcessDefinition definition) { BpmnValidator.Validate(definition); }

    #endregion
}
