using System.Collections.Generic;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.Commands;

/// <summary>Requests all push subscriptions for an owner, optionally filtered by provider.</summary>
/// <param name="Owner">The subscription owner to query.</param>
/// <param name="Provider">The optional transport provider to filter on.</param>
public sealed record GetPushSubscriptionsQuery(
    string  Owner,
    string? Provider = null
) : IQuery<IReadOnlyList<SchemataPushSubscription>>;
