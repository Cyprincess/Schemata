using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Prunes expired / revoked / consumed tokens via
///     <see cref="ITokenManager{TToken}" />.  Registered as an hourly cron
///     entry on <see cref="SchemataSchedulingOptions.Jobs" />.
/// </summary>
public sealed class TokenCleanupJob<TToken>(ITokenManager<TToken> tokens, TimeProvider? time = null) : IScheduledJob
    where TToken : SchemataToken
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IScheduledJob Members

    public Task ExecuteAsync(JobContext context, CancellationToken ct) {
        return tokens.PruneAsync(_time.GetUtcNow().UtcDateTime, ct);
    }

    #endregion
}
