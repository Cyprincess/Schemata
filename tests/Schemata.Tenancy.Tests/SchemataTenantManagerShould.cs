using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Services;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenantManagerShould
{
    [Fact]
    public async Task DeleteAsync_Evicts_Tenant_Provider_From_Cache() {
        var tenantId = Guid.NewGuid();
        var tenant   = new SchemataTenant<Guid> { Id = 1, TenantId = tenantId };

        var tenants = new FakeTenantRepository();
        var hosts   = new FakeHostRepository();
        var cache   = new Mock<ITenantProviderCache>();

        var manager = new SchemataTenantManager<SchemataTenant<Guid>, Guid>(tenants, hosts, cache.Object);

        await manager.DeleteAsync(tenant, CancellationToken.None);

        cache.Verify(c => c.Remove(tenantId.ToString()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Does_Not_Evict_When_Tenant_Has_No_TenantId() {
        var tenant  = new SchemataTenant<Guid> { Id = 1, TenantId = null };
        var tenants = new FakeTenantRepository();
        var hosts   = new FakeHostRepository();
        var cache   = new Mock<ITenantProviderCache>(MockBehavior.Strict);

        var manager = new SchemataTenantManager<SchemataTenant<Guid>, Guid>(tenants, hosts, cache.Object);

        await manager.DeleteAsync(tenant, CancellationToken.None);

        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FindByHost_Resolves_Tenant_Through_Association_Table() {
        var tenant = new SchemataTenant<Guid> { Id = 42, TenantId = Guid.NewGuid() };

        var tenants = new FakeTenantRepository();
        tenants.Items.Add(tenant);

        var hosts = new FakeHostRepository();
        hosts.Items.Add(new() { Id = 1, SchemataTenantId = 42, Host = "example.test" });

        var manager = new SchemataTenantManager<SchemataTenant<Guid>, Guid>(
            tenants,
            hosts,
            new Mock<ITenantProviderCache>().Object
        );

        var resolved = await manager.FindByHost("example.test", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(42, resolved!.Id);
    }

    [Fact]
    public async Task FindByHost_Returns_Null_When_No_Host_Row_Matches() {
        var manager = new SchemataTenantManager<SchemataTenant<Guid>, Guid>(
            new FakeTenantRepository(),
            new FakeHostRepository(),
            new Mock<ITenantProviderCache>().Object
        );

        Assert.Null(await manager.FindByHost("missing.test", CancellationToken.None));
    }

    #region Nested type: FakeHostRepository

    private sealed class FakeHostRepository : FakeRepository<SchemataTenantHost>;

    #endregion

    #region Nested type: FakeRepository

    private class FakeRepository<T> : IRepository<T>
        where T : class
    {
        public List<T> Items { get; } = [];

        #region IRepository<T> Members

        public AdviceContext AdviceContext { get; } = new(new ServiceCollection().BuildServiceProvider());

        public IAsyncEnumerable<T> AsAsyncEnumerable() { return ToAsync(Items); }

        public IQueryable<T> AsQueryable() { return Items.AsQueryable(); }

        public IAsyncEnumerable<TResult> ListAsync<TResult>(
            Func<IQueryable<T>, IQueryable<TResult>>? predicate,
            CancellationToken                         ct = default
        ) {
            return ToAsync(
                (predicate is null ? (IQueryable<TResult>)Items.AsQueryable() : predicate(Items.AsQueryable())).ToList()
            );
        }

        public IAsyncEnumerable<TResult> SearchAsync<TResult>(
            Func<IQueryable<T>, IQueryable<TResult>>? predicate,
            CancellationToken                         ct = default
        ) {
            return ListAsync(predicate, ct);
        }

        public ValueTask<T?> GetAsync(T entity, CancellationToken ct = default) { return new(default(T?)); }

        public ValueTask<TResult?> GetAsync<TResult>(T entity, CancellationToken ct = default) {
            return new(default(TResult));
        }

        public ValueTask<T?> FindAsync(object[] keys, CancellationToken ct = default) { return new(default(T?)); }

        public ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default) {
            return new(default(TResult));
        }

        public ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
            Func<IQueryable<T>, IQueryable<TResult>>? predicate,
            CancellationToken                         ct = default
        ) {
            var q = predicate is null ? (IQueryable<TResult>)Items.AsQueryable() : predicate(Items.AsQueryable());
            return new(q.FirstOrDefault());
        }

        public ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
            Func<IQueryable<T>, IQueryable<TResult>>? predicate,
            CancellationToken                         ct = default
        ) {
            var q = predicate is null ? (IQueryable<TResult>)Items.AsQueryable() : predicate(Items.AsQueryable());
            return new(q.SingleOrDefault());
        }

        public ValueTask<bool> AnyAsync<TResult>(
            Func<IQueryable<T>, IQueryable<TResult>>? predicate,
            CancellationToken                         ct = default
        ) {
            var q = predicate is null ? (IQueryable<TResult>)Items.AsQueryable() : predicate(Items.AsQueryable());
            return new(q.Any());
        }

        public ValueTask<int> CountAsync<TResult>(
            Func<IQueryable<T>, IQueryable<TResult>>? predicate,
            CancellationToken                         ct = default
        ) {
            var q = predicate is null ? (IQueryable<TResult>)Items.AsQueryable() : predicate(Items.AsQueryable());
            return new(q.Count());
        }

        public ValueTask<long> LongCountAsync<TResult>(
            Func<IQueryable<T>, IQueryable<TResult>>? predicate,
            CancellationToken                         ct = default
        ) {
            var q = predicate is null ? (IQueryable<TResult>)Items.AsQueryable() : predicate(Items.AsQueryable());
            return new(q.LongCount());
        }

        public Task AddAsync(T entity, CancellationToken ct = default) {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) {
            Items.AddRange(entities);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(T entity, CancellationToken ct = default) { return Task.CompletedTask; }

        public Task RemoveAsync(T entity, CancellationToken ct = default) {
            Items.Remove(entity);
            return Task.CompletedTask;
        }

        public Task RemoveRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) {
            foreach (var e in entities.ToList()) Items.Remove(e);
            return Task.CompletedTask;
        }

        public ValueTask<int> CommitAsync(CancellationToken ct = default) { return new(0); }

        public void Detach(T entity) { }

        public IRepository<T> Once() { return this; }

        public IRepository<T> SuppressAddValidation() { return this; }

        public IRepository<T> SuppressUpdateValidation() { return this; }

        public IRepository<T> SuppressConcurrency() { return this; }

        public IRepository<T> SuppressQuerySoftDelete() { return this; }

        public IRepository<T> SuppressSoftDelete() { return this; }

        public IRepository<T> SuppressTimestamp() { return this; }

        public IRepository<T> SuppressOwner() { return this; }

        public IRepository<T> SuppressQueryOwner() { return this; }

        #endregion

        private static async IAsyncEnumerable<TResult> ToAsync<TResult>(IEnumerable<TResult> source) {
            foreach (var item in source) {
                yield return item;
                await Task.Yield();
            }
        }
    }

    #endregion

    #region Nested type: FakeTenantRepository

    private sealed class FakeTenantRepository : FakeRepository<SchemataTenant<Guid>>;

    #endregion
}
