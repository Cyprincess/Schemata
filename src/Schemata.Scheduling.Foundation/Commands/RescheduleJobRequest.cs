using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Commands;

/// <summary>Requests re-arming of a persisted job.</summary>
/// <param name="Job">Persisted job to re-arm.</param>
/// <param name="PreparedContext">Existing unfinished operation context, when supplied by recovery.</param>
public sealed record RescheduleJobRequest(
    SchemataJob Job,
    JobContext? PreparedContext
) : ICommand, IJobScoped
{
    public string JobCanonicalName => Job.CanonicalName ?? Job.Name ?? string.Empty;
}
