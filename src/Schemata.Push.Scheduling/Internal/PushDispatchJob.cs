using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Push.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Attributes;

namespace Schemata.Push.Scheduling.Internal;

/// <summary>
///     Scheduled job that runs a deferred push dispatch. The <see cref="PushContext" /> is rebuilt
///     from the persisted arguments and fanned out through <see cref="IPushService" />, so a
///     scheduled send survives a host restart and is observed as an ordinary
///     <c>operations/{operation}</c> long-running operation. The per-transport results are recorded
///     as the operation response.
/// </summary>
[ScheduledJob(JobKey)]
public sealed class PushDispatchJob : IScheduledJob
{
    /// <summary>Stable scheduler key persisted on scheduled push job and execution rows.</summary>
    public const string JobKey = "schemata.push.send";

    private readonly IServiceProvider _services;

    /// <summary>Creates the deferred push dispatch job.</summary>
    /// <param name="services">The service provider supplying the push service.</param>
    public PushDispatchJob(IServiceProvider services) { _services = services; }

    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        var pushContext = context.ArgsJson is { } json
            ? JsonSerializer.Deserialize<PushContext>(json, SchemataJson.Default)
            : null;

        if (pushContext is null) {
            throw new InvalidOperationException("Scheduled push job is missing its dispatch context.");
        }

        var push    = _services.GetRequiredService<IPushService>();
        var results = new List<TransportResult>();
        await foreach (var result in push.SendAsync(pushContext, ct)) {
            results.Add(result);
        }

        if (context.Execution is { } execution) {
            execution.Output = JsonSerializer.Serialize(results, SchemataJson.Default);
        }
    }

    #endregion
}
