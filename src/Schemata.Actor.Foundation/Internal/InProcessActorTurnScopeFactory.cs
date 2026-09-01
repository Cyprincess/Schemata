using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Internal;

/// <summary>
///     Default <see cref="IActorTurnScopeFactory" />: creates a scope from the host root and
///     restores every registered <see cref="IMessageContextPropagator" /> into it before handing
///     the scope back to the turn dispatcher. Registered with <c>TryAdd</c> so a capability such
///     as multi-tenancy can override it with <c>Replace</c> (see <see cref="IActorTurnScopeFactory" />).
/// </summary>
public sealed class InProcessActorTurnScopeFactory(IServiceScopeFactory scopeFactory) : IActorTurnScopeFactory
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyItems = new Dictionary<string, string?>();

    #region IActorTurnScopeFactory Members

    public async ValueTask<AsyncServiceScope> CreateAsync(MessageContext? context, CancellationToken ct = default) {
        var scope = scopeFactory.CreateAsyncScope();
        try {
            var items = context?.Items ?? EmptyItems;

            foreach (var propagator in scope.ServiceProvider.GetServices<IMessageContextPropagator>()) {
                await propagator.RestoreAsync(items, scope.ServiceProvider, ct);
            }

            return scope;
        } catch {
            // The scope was already allocated before the failing propagator ran; nobody else has
            // a reference to it, so this method must release it itself before the caller ever
            // sees the exception, or it leaks for the lifetime of the turn dispatcher's failure.
            await scope.DisposeAsync();
            throw;
        }
    }

    #endregion
}
