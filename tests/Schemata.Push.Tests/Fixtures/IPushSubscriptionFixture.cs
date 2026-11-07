using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests.Fixtures;

/// <summary>Provider-boundary surface shared by the EF Core and LINQ to DB fixtures.</summary>
public interface IPushSubscriptionFixture : IAsyncLifetime
{
    Task<SchemataPushSubscription?> AddAsync(AddPushSubscriptionRequest request);

    Task RemoveAsync(RemovePushSubscriptionRequest request);

    void BlockUndelete();

    /// <summary>Returns every stored row, including soft-deleted ones.</summary>
    Task<List<SchemataPushSubscription>> SubscriptionsAsync();
}

internal sealed class BlockPushSubscriptionCreate : IRepositoryAddAdvisor<SchemataPushSubscription>
{
    public int Order => int.MaxValue;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                         context,
        IRepository<SchemataPushSubscription> repository,
        SchemataPushSubscription              entity,
        MutationOptions                       options,
        CancellationToken                     ct
    ) {
        return Task.FromResult(AdviseResult.Block);
    }
}

internal sealed class BlockPushSubscriptionUndelete : IRepositoryUpdateAdvisor<SchemataPushSubscription>
{
    public bool Enabled { get; set; }

    public int Order => int.MaxValue;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                         context,
        IRepository<SchemataPushSubscription> repository,
        SchemataPushSubscription              entity,
        MutationOptions                       options,
        CancellationToken                     ct
    ) {
        return Task.FromResult(Enabled ? AdviseResult.Block : AdviseResult.Continue);
    }
}
