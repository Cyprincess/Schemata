using System;
using Schemata.Abstractions.Resource;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     <c>:wait</c> handler on <see cref="SchemataJobExecution" />, mirroring
///     <c>WaitOperation</c> on the <c>google.longrunning.Operations</c> service. That RPC declares
///     no HTTP binding, so the route is Schemata's; AIP-151 supplies the <c>Operation</c> shape.
///     Performs server-side bounded polling capped at 30 seconds and returns the
///     current snapshot once the row reaches a terminal state or the deadline
///     elapses.
/// </summary>
public sealed class WaitOperationHandler(IOperationService operations, TimeProvider? time = null)
    : IRequestHandler<WaitOperationRequest, Operation>
{
    /// <summary>
    ///     Maximum server-side wait duration accepted by the handler.
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<Operation> HandleAsync(
        WaitOperationRequest request,
        CancellationToken ct = default
    ) {
        var operation = request.CanonicalName ?? string.Empty;
        using var deadline = new CancellationTokenSource(GetEffectiveTimeout(request.Timeout), _time);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        try {
            return await operations.WaitAsync(operation, bounded.Token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return await operations.GetAsync(operation, ct);
        }
    }

    /// <summary>
    ///     Returns the bounded wait duration used for a request.
    /// </summary>
    public static TimeSpan GetEffectiveTimeout(TimeSpan? requested) {
        if (requested is null || requested.Value <= TimeSpan.Zero) {
            return MaxWait;
        }

        return requested.Value < MaxWait ? requested.Value : MaxWait;
    }
}
