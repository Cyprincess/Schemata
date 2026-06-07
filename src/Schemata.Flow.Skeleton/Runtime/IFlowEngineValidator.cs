using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

public interface IFlowEngineValidator
{
    string EngineName { get; }

    void Validate(ProcessDefinition definition);
}
