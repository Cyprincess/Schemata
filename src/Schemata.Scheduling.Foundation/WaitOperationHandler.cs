using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     AIP-151 <c>:wait</c> handler on <see cref="SchemataJobExecution" />.
///     Performs server-side bounded polling capped at 30 seconds and returns the
///     current snapshot once the row reaches a terminal state or the deadline
///     elapses.
/// </summary>
public sealed class WaitOperationHandler(IOperationService operations, TimeProvider? time = null)
    : IResourceMethodHandler<SchemataJobExecution, WaitOperationRequest, Operation>
{
    /// <summary>Maximum server-side wait duration accepted by the handler.</summary>
    public static readonly  TimeSpan MaxWait      = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IResourceMethodHandler<SchemataJobExecution, WaitOperationRequest, Operation> Members

    public async ValueTask<Operation> InvokeAsync(
        string?               name,
        WaitOperationRequest  request,
        SchemataJobExecution? entity,
        ClaimsPrincipal?      principal,
        CancellationToken     ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        var operation = entity.CanonicalName ?? $"operations/{entity.Uid:n}";
        using var deadline = new CancellationTokenSource(GetEffectiveTimeout(request.Timeout), _time);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        try {
            return await operations.WaitAsync(operation, bounded.Token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            return await operations.GetAsync(operation, ct);
        }
    }

    #endregion

    /// <summary>Returns the bounded wait duration used for a request.</summary>
    public static TimeSpan GetEffectiveTimeout(TimeSpan? requested) {
        if (requested is null || requested.Value <= TimeSpan.Zero) {
            return MaxWait;
        }

        return requested.Value < MaxWait ? requested.Value : MaxWait;
    }
}
