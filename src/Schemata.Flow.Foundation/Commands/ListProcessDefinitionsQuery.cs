using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Commands;

public sealed record ListProcessDefinitionsQuery(string? DefinitionName = null, string? DefinitionVersion = null) : IQuery<IReadOnlyList<ProcessDefinitionInfo>>;
