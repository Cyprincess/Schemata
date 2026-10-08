using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Prunes expired / revoked / consumed tokens via
///     <see cref="ITokenStore{SchemataToken}" />.  Registered as an hourly cron
///     entry on <see cref="SchemataSchedulingOptions.Jobs" />.
/// </summary>
[ScheduledJob(JobKey)]
public sealed class TokenCleanupJob(ITokenStore<SchemataToken> tokens) : IScheduledJob
{
    /// <summary>Stable scheduler key persisted on token-cleanup job and execution rows.</summary>
    public const string JobKey = "schemata.authorization.token.cleanup";

    #region IScheduledJob Members

    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        return tokens.PruneAsync(ct);
    }

    #endregion
}
