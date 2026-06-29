using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Foundation;

/// <summary>Repository helpers for <see cref="SchemataEventSubscription" />.</summary>
public static class SchemataEventSubscriptionExtensions
{
    /// <summary>
    ///     Returns subscriptions whose <see cref="SchemataEventSubscription.EventType" /> matches and,
    ///     when <paramref name="correlationKey" /> is supplied, whose
    ///     <see cref="SchemataEventSubscription.CorrelationKey" /> equals it. The query streams through
    ///     the build-query advisor pipeline of <paramref name="repository" />.
    /// </summary>
    /// <param name="repository">The subscription repository.</param>
    /// <param name="eventType">The wire-format event name to match.</param>
    /// <param name="correlationKey">The optional correlation key narrowing the result set.</param>
    /// <param name="ct">A cancellation token.</param>
    public static IAsyncEnumerable<SchemataEventSubscription> ListMatchingAsync(
        this IRepository<SchemataEventSubscription> repository,
        string                                      eventType,
        string?                                     correlationKey = null,
        CancellationToken                           ct             = default
    ) {
        return repository.ListAsync(
            q => q.Where(s => s.EventType == eventType
                           && (correlationKey == null || s.CorrelationKey == correlationKey)), ct);
    }
}
