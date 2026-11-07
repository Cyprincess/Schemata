using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Foundation.Services;

/// <summary>Rotates every configured security key slot through the domain rotation service.</summary>
/// <typeparam name="TSecurity">Concrete security entity type.</typeparam>
[ScheduledJob(JobKey)]
public sealed class SecurityKeyRotationJob<TSecurity>(
    SecurityKeyRotationService<TSecurity> rotation,
    IOptions<SchemataSecurityOptions> options
) : IScheduledJob where TSecurity : SchemataSecurity, new()
{
    /// <summary>Stable scheduler key for periodic security key rotation.</summary>
    public const string JobKey = "schemata.security.keys.rotate";

    /// <inheritdoc />
    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        foreach (var configured in options.Value.Rotations) {
            await rotation.RotateAsync(configured.Parent, configured.Usage, configured.Algorithm, ct);
        }
    }
}
