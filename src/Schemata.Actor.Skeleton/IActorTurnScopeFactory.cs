using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Skeleton;

/// <summary>
///     Creates the dependency-injection scope for one actor turn. Implementations decide which
///     provider that scope descends from.
/// </summary>
/// <remarks>
///     A turn dispatcher must always resolve its scope through this factory rather than calling
///     <see cref="ServiceProviderServiceExtensions.CreateAsyncScope(IServiceScopeFactory)" />
///     directly. Some hosting concerns — multi-tenancy chief among them — must resolve identity
///     and rebuild ambient context <em>before</em> the final scope is created, because a scope
///     already bound to one provider cannot be retargeted afterward. The default implementation,
///     registered with <c>TryAdd</c> so a capability such as multi-tenancy can override it with
///     <c>Replace</c>, simply creates a scope from the host root.
/// </remarks>
public interface IActorTurnScopeFactory
{
    /// <summary>Creates the scope for one turn and restores any ambient state carried by <paramref name="context" />.</summary>
    /// <param name="context">
    ///     The sender-captured ambient state to restore in the new scope, or
    ///     <see langword="null" /> when there is none to propagate.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The scope for the turn. The caller disposes it once the turn completes.</returns>
    ValueTask<AsyncServiceScope> CreateAsync(MessageContext? context, CancellationToken ct = default);
}
