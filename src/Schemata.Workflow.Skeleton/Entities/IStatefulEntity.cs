using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Entities;

public interface IStatefulEntity : IIdentifier, IStateful, ITimestamp;
