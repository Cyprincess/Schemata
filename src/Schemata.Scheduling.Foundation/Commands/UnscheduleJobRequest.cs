using Schemata.Messaging.Skeleton;

namespace Schemata.Scheduling.Foundation.Commands;

/// <summary>Requests removal of a scheduled job and cancellation of its future pending executions.</summary>
/// <param name="JobCanonicalName">Canonical name of the job to unschedule.</param>
public sealed record UnscheduleJobRequest(string JobCanonicalName) : ICommand;
