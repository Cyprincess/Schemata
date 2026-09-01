using System.Collections.Generic;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Commands;

/// <summary>Requests scheduling of a job and its next pending execution.</summary>
/// <param name="Job">Job definition to schedule.</param>
/// <param name="Variables">Variables supplied by the variables overload.</param>
/// <param name="ReplaceVariables">Whether <paramref name="Variables" /> replaces the value already stored on <paramref name="Job" />.</param>
public sealed record ScheduleJobRequest(
    SchemataJob                           Job,
    IReadOnlyDictionary<string, string?>? Variables,
    bool                                  ReplaceVariables = false
) : ICommand;
