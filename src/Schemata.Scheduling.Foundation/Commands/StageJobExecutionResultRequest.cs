using System;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Commands;

/// <summary>Stages the job-row result of a finished execution for the scheduling writer.</summary>
/// <param name="JobCanonicalName">Canonical or plain name identifying the persisted job row.</param>
/// <param name="State">Job state after the execution reached its terminal state.</param>
/// <param name="RecentRunTime">End time of the finished execution.</param>
/// <param name="RecentError">Error message when the execution failed, otherwise <see langword="null" />.</param>
/// <param name="NextRunTime">Next occurrence for a recurring active job, otherwise <see langword="null" />.</param>
public sealed record StageJobExecutionResultRequest(
    string    JobCanonicalName,
    JobState  State,
    DateTime? RecentRunTime,
    string?   RecentError,
    DateTime? NextRunTime
) : ICommand, IJobScoped;
