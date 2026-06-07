using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Side-effect hook for <see cref="IEventBus" /> implementations. Observers
///     see the dispatch context after publish-side advisors return
///     <c>Continue</c> and again after handler dispatch settles.
/// </summary>
public interface IEventLifecycleObserver
{
    /// <summary>
    ///     Fires after the publish-side advisor pipeline returns <c>Continue</c>
    ///     and before handler dispatch begins.
    /// </summary>
    Task OnPublishedAsync(EventContext context, CancellationToken ct = default);

    /// <summary>
    ///     Fires after handler dispatch settles (success, failure, or
    ///     short-circuit). <see cref="EventContext.Result" /> or
    ///     <see cref="EventContext.Exception" /> reflects the outcome.
    /// </summary>
    Task OnConsumedAsync(EventContext context, CancellationToken ct = default);
}
