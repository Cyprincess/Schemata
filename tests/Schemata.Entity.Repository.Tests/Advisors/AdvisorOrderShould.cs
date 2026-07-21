// TestRepository below is a structural type-token: it serves as TImpl in the
// AddRepository<TEntity, TImpl>() generic registration, a role a Moq proxy cannot fill.
// It is retained as a hand-written stub as a sanctioned exception to the Moq-only rule.
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Owner;
using Schemata.Entity.Owner.Advisors;
using Schemata.Entity.Repository.Advisors;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdvisorOrderShould
{
    [Fact]
    public void DefaultOrders_Match_AddAdvisorChain() {
        Assert.Equal(
            [
                100_000_000,
                110_000_000,
                120_000_000,
                121_000_000,
                130_000_000,
                140_000_000,
                150_000_000,
                160_000_000,
                900_000_000,
            ],
            [
                AdviceAddTimestamp.DefaultOrder,
                AdviceAddConcurrency.DefaultOrder,
                AdviceAddCanonicalName.DefaultOrder,
                AdviceAddOwner.DefaultOrder,
                AdviceAddValidation.DefaultOrder,
                AdviceValidateResourceReferences.DefaultOrder,
                AdviceValidateResourceReferenceExistence.DefaultOrder,
                AdviceAddUniqueness.DefaultOrder,
                AdviceAddSoftDelete.DefaultOrder,
            ]);
    }

    [Fact]
    public void RegisteredAddAdvisors_AreStrictlyAscendingInAddAdvisorChain() {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IOwnerResolver<AdvisorEntity>>());
        services.AddRepository<AdvisorEntity, TestRepository>().UseOwner();

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();
        var advisors = scope.ServiceProvider.GetServices<IRepositoryAddAdvisor<AdvisorEntity>>().OrderBy(advisor => advisor.Order).ToList();

        Assert.Collection(
            advisors,
            advisor => Assert.IsType<AdviceAddTimestamp<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceAddConcurrency<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceAddCanonicalName<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceAddOwner<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceAddValidation<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceValidateResourceReferences<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceValidateResourceReferenceExistence<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceAddUniqueness<AdvisorEntity>>(advisor),
            advisor => Assert.IsType<AdviceAddSoftDelete<AdvisorEntity>>(advisor));

        Assert.All(advisors.Zip(advisors.Skip(1)), pair => Assert.True(pair.First.Order < pair.Second.Order));
    }

    public sealed class AdvisorEntity;

    private sealed class TestRepository(System.IServiceProvider serviceProvider) : RepositoryBase<AdvisorEntity>(serviceProvider)
    {
        public override Task AddAsync(AdvisorEntity entity, CancellationToken ct = default) => Task.CompletedTask;

        public override Task UpdateAsync(AdvisorEntity entity, CancellationToken ct = default) => Task.CompletedTask;

        public override Task RemoveAsync(AdvisorEntity entity, CancellationToken ct = default) => Task.CompletedTask;

        protected override ConfiguredCancelableAsyncEnumerable<TResult> AsAsyncEnumerable<TResult>(
            IQueryable<TResult> query,
            CancellationToken   ct
        ) => throw new System.NotSupportedException();

        protected override Task<TResult?> FirstOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct)
            where TResult : default => throw new System.NotSupportedException();

        protected override Task<TResult?> SingleOrDefaultAsync<TResult>(IQueryable<TResult> query, CancellationToken ct)
            where TResult : default => throw new System.NotSupportedException();

        protected override Task<bool> AnyAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) => throw new System.NotSupportedException();

        protected override Task<int> CountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) => throw new System.NotSupportedException();

        protected override Task<long> LongCountAsync<TResult>(IQueryable<TResult> query, CancellationToken ct) => throw new System.NotSupportedException();

        protected override IQueryable<AdvisorEntity> AsQueryable() => Enumerable.Empty<AdvisorEntity>().AsQueryable();

        protected override IUnitOfWork CreateUnitOfWork() => throw new System.NotSupportedException();

        protected override void AttachContext(IUnitOfWork uow) { }

        protected override void DisposeContext() { }
    }
}
