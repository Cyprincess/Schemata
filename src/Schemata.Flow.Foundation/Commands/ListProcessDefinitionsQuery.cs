using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

/// <summary>Requests the projection of every registered Flow process definition.</summary>
public sealed record ListProcessDefinitionsQuery : IQuery<IReadOnlyList<ProcessDefinitionInfo>>;
