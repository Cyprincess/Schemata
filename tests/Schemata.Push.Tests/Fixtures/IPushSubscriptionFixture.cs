using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests.Fixtures;

/// <summary>Provider-boundary surface shared by the EF Core and LINQ to DB fixtures.</summary>
public interface IPushSubscriptionFixture : IAsyncLifetime
{
    Task<SchemataPushSubscription> AddAsync(AddPushSubscriptionRequest request);

    Task RemoveAsync(RemovePushSubscriptionRequest request);

    /// <summary>Returns every stored row, including soft-deleted ones.</summary>
    Task<List<SchemataPushSubscription>> SubscriptionsAsync();
}
