using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Validates that a <see cref="ProcessDefinition" /> is well-formed for a specific engine.</summary>
public interface IFlowEngineValidator
{
    /// <summary>Engine name this validator targets (matches the <see cref="IFlowRuntime" /> key).</summary>
    string EngineName { get; }

    /// <summary>Throws when <paramref name="definition" /> is not executable on this engine.</summary>
    void Validate(ProcessDefinition definition);
}
