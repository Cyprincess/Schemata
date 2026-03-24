using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Entities;

/// <summary>
/// Represents an entity that participates in a workflow and has an identifier, state, and timestamps.
/// </summary>
public interface IStatefulEntity : IIdentifier, IStateful, ITimestamp;
