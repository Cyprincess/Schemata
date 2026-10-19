using System.Collections.Generic;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Commands;

/// <summary>Requests scheduling of a job and its next pending execution.</summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <param name="Job">Job definition to schedule.</param>
/// <param name="Variables">Variables supplied by the variables overload.</param>
/// <param name="ReplaceVariables">Whether <paramref name="Variables" /> replaces the value already stored on <paramref name="Job" />.</param>
public sealed record ScheduleJobRequest<TJob>(
    TJob                                  Job,
    IReadOnlyDictionary<string, string?>? Variables,
    bool                                  ReplaceVariables = false
) : ICommand
    where TJob : SchemataJob;
