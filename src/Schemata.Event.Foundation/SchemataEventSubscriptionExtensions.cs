using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Foundation;

/// <summary>Repository helpers for <see cref="SchemataEventSubscription" />.</summary>
public static class SchemataEventSubscriptionExtensions
{
    /// <summary>
    ///     Returns subscriptions whose <see cref="SchemataEventSubscription.EventType" /> matches and whose
    ///     <see cref="SchemataEventSubscription.CorrelationFilter" /> is satisfied by
    ///     <paramref name="correlation" />. The query streams through the build-query advisor pipeline of
    ///     <paramref name="repository" />; the correlation filter is evaluated per streamed row before the
    ///     subscription reaches handler resolution.
    /// </summary>
    /// <param name="repository">The subscription repository.</param>
    /// <param name="eventType">The wire-format event name to match.</param>
    /// <param name="correlation">The envelope's business correlation metadata, if the publisher supplied any.</param>
    /// <param name="ct">A cancellation token.</param>
    public static async IAsyncEnumerable<SchemataEventSubscription> ListMatchingAsync(
        this IRepository<SchemataEventSubscription> repository,
        string                                      eventType,
        IReadOnlyDictionary<string, string>?        correlation = null,
        [EnumeratorCancellation] CancellationToken  ct          = default
    ) {
        await foreach (var subscription in repository.ListAsync(q => q.Where(s => s.EventType == eventType), ct)) {
            if (subscription.MatchesCorrelation(correlation)) {
                yield return subscription;
            }
        }
    }

    /// <summary>
    ///     Returns whether the subscription's <see cref="SchemataEventSubscription.CorrelationFilter" /> is
    ///     satisfied by the envelope <paramref name="correlation" /> metadata: a null or empty filter matches
    ///     every event; otherwise every filtered pair must be present with an ordinal-equal value.
    /// </summary>
    /// <param name="subscription">The subscription row being evaluated.</param>
    /// <param name="correlation">The envelope's business correlation metadata.</param>
    public static bool MatchesCorrelation(
        this SchemataEventSubscription       subscription,
        IReadOnlyDictionary<string, string>? correlation
    ) {
        var filter = subscription.CorrelationFilter;
        if (filter is null || filter.Count == 0) {
            return true;
        }

        if (correlation is null) {
            return false;
        }

        foreach (var (key, value) in filter) {
            if (!correlation.TryGetValue(key, out var actual) || !string.Equals(actual, value, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }
}
