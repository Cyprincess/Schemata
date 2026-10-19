using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;
using PrimaryKeyAttribute = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Tenancy.Tests;

[Trait("Layer", "Unit")]
public sealed class TenantRepositoryRegistrationShould
{
    private static readonly Guid TenantUid = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUid  = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void Register_Host_Binding_And_Open_Generic_Advisors_Once() {
        var services = new ServiceCollection();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(_ => { }).UseRepositories(_ => { });

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TenantRepositoryBinding)
            && ReferenceEquals(descriptor.ImplementationInstance, TenantRepositoryBinding.Host));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRepositoryAddAdvisor<>)
            && descriptor.ImplementationType == typeof(AdviceAddTenant<>));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRepositoryBuildQueryAdvisor<>)
            && descriptor.ImplementationType == typeof(AdviceBuildQueryTenant<>));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRepositoryUpdateAdvisor<>)
            && descriptor.ImplementationType == typeof(AdviceUpdateTenant<>));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRepositoryRemoveAdvisor<>)
            && descriptor.ImplementationType == typeof(AdviceRemoveTenant<>));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IRepository<>));
    }

    [Fact]
    public void Compose_Tenant_Bindings_And_Invoke_The_Callback_With_The_Verified_Identifier() {
        var services   = new ServiceCollection();
        var identifiers = new List<Guid>();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder => identifiers.Add(builder.TenantUid));

        Assert.Empty(identifiers);

        var tenant = Compose(services, TenantUid);

        Assert.Equal([TenantUid], identifiers);
        Assert.Contains(tenant, descriptor => descriptor.ServiceType == typeof(TenantRepositoryBinding)
            && descriptor.ImplementationInstance is TenantRepositoryBinding binding && binding.Uid == TenantUid);
        Assert.Contains(tenant, descriptor => descriptor.ServiceType == typeof(RepositoryBinding)
            && descriptor.ImplementationInstance is RepositoryBinding binding && binding.Namespace == $"tenant:{TenantUid:n}");
        Assert.DoesNotContain(tenant, descriptor => descriptor.ServiceType == typeof(IRepositoryAddAdvisor<>));
    }

    [Fact]
    public void Leave_Repository_And_Mutation_Pairing_To_The_Tenant_Registration() {
        var paired = new ServiceCollection();
        new SchemataTenancyBuilder<SchemataTenant>(paired).UseRepositories(builder => {
            builder.Services.AddTransient<IRepository<Row>, StubRepository<Row>>();
            builder.Services.AddScoped<IResourceMutation<Row>, ResourceMutation<Row>>();
        });
        var unpaired = new ServiceCollection();
        new SchemataTenancyBuilder<SchemataTenant>(unpaired).UseRepositories(builder
            => builder.Services.AddTransient<IRepository<Row>, StubRepository<Row>>());

        var pairedTenant   = Compose(paired, TenantUid);
        var unpairedTenant = Compose(unpaired, TenantUid);

        Assert.Contains(pairedTenant, descriptor => descriptor.ServiceType == typeof(IResourceMutation<Row>)
            && descriptor.ImplementationType == typeof(ResourceMutation<Row>));
        Assert.DoesNotContain(unpairedTenant, descriptor => descriptor.ServiceType == typeof(IResourceMutation<Row>));
    }

    [Fact]
    public void Expose_Only_Explicit_Database_Registration_Calls() {
        Assert.Null(typeof(TenantRepositoryBuilder<SchemataTenant>).GetMethod("UseSharedTable"));
        Assert.Null(typeof(TenantRepositoryBuilder<SchemataTenant>).GetProperty("IsRegistrationProbe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public void Reject_Tenant_Catalog_Partition_Registration() {
        var services = new ServiceCollection();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder
            => builder.UseDatabase<SchemataTenant, CatalogHost, StubRepository<CatalogHost>>(_ => { }));

        Assert.Throws<ArgumentException>(() => Compose(services, TenantUid));
    }

    [Fact]
    public void Require_A_Source_For_Every_Database_Registration() {
        var services = new ServiceCollection();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder
            => builder.UseDatabase<SchemataTenant, Row, StubRepository<Row>>(_ => { }));

        var error = Assert.Throws<InvalidOperationException>(() => Compose(services, TenantUid));
        Assert.Contains("source", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Require_A_Verified_Identifier_Before_Composing() {
        var services = new ServiceCollection();
        new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(_ => { });

        Assert.Throws<InvalidOperationException>(() => Compose(services, Guid.Empty));
    }

    [Fact]
    public async Task Stamp_The_Bound_Identity_Regardless_Of_The_Ambient_Tenant() {
        using var provider = BindingProvider(TenantUid);
        var advisor        = new AdviceAddTenant<Row>();
        var row            = new Row { TenantUid = OtherUid };
        using var ambient  = TenantContext.Enter(new(OtherUid));

        Assert.Equal(AdviseResult.Continue,
            await advisor.AdviseAsync(new(provider), new Mock<IRepository<Row>>().Object, row, default));
        Assert.Equal(TenantUid, row.TenantUid);
        Assert.True(advisor.Order < AdviceAddCanonicalName.DefaultOrder);
        Assert.True(advisor.Order < AdviceAddUniqueness.DefaultOrder);
    }

    [Fact]
    public async Task Filter_Queries_By_The_Bound_Identity() {
        using var provider = BindingProvider(TenantUid);
        var rows = new List<Row> {
            new() { Uid = Guid.NewGuid(), TenantUid = TenantUid },
            new() { Uid = Guid.NewGuid(), TenantUid = OtherUid },
            new() { Uid = Guid.NewGuid() },
        };
        var container = new QueryContainer<Row>(new Mock<IRepository<Row>>().Object, rows.AsQueryable());

        Assert.Equal(AdviseResult.Continue,
            await new AdviceBuildQueryTenant<Row>().AdviseAsync(new(provider), container));
        Assert.Equal(TenantUid, Assert.Single(container.Query).TenantUid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_Cross_Tenant_Mutation_Through_The_Advisor(bool remove) {
        using var provider = BindingProvider(TenantUid);
        var repository     = new Mock<IRepository<Row>>();
        repository.Setup(value => value.GetOriginalAsync(It.IsAny<Row>(), It.IsAny<System.Linq.Expressions.Expression<Func<Row, Guid?>>>(), It.IsAny<System.Threading.CancellationToken>(), It.IsAny<QueryOptions>()))
            .ReturnsAsync(new OriginalResult<Guid?>(true, OtherUid));
        var row = new Row { TenantUid = OtherUid };

        await Assert.ThrowsAsync<PermissionDeniedException>(() => remove
            ? new AdviceRemoveTenant<Row>().AdviseAsync(new(provider), repository.Object, row, default)
            : new AdviceUpdateTenant<Row>().AdviseAsync(new(provider), repository.Object, row, default));
        Assert.Equal(OtherUid, row.TenantUid);
    }

    [Fact]
    public async Task Ignore_Entities_Without_The_Tenant_Trait() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var repository     = new Mock<IRepository<Plain>>().Object;
        var row            = new Plain();
        var container      = new QueryContainer<Plain>(repository, new List<Plain> { row, new() }.AsQueryable());

        Assert.Equal(AdviseResult.Continue, await new AdviceAddTenant<Plain>().AdviseAsync(new(provider), repository, row, default));
        Assert.Equal(AdviseResult.Continue, await new AdviceBuildQueryTenant<Plain>().AdviseAsync(new(provider), container));
        Assert.Equal(AdviseResult.Continue, await new AdviceUpdateTenant<Plain>().AdviseAsync(new(provider), repository, row, default));
        Assert.Equal(AdviseResult.Continue, await new AdviceRemoveTenant<Plain>().AdviseAsync(new(provider), repository, row, default));
        Assert.Equal(2, container.Query.Count());
    }

    private static ServiceProvider BindingProvider(Guid uid) {
        return new ServiceCollection().AddSingleton(new TenantRepositoryBinding(uid)).BuildServiceProvider();
    }

    private static List<ServiceDescriptor> Compose(IServiceCollection services, Guid uid) {
        var tenant = new ServiceCollection();
        using var root = services.BuildServiceProvider();
        foreach (var configure in root.GetRequiredService<IOptions<SchemataTenancyOptions>>().Value.DynamicOverrides) {
            configure(uid.ToString(), tenant, null!);
        }

        return tenant.ToList();
    }

    [PrimaryKey(nameof(Uid))]
    public sealed class Row : ITenant, IIdentifier, IConcurrency
    {
        public Guid? TenantUid { get; set; }
        public Guid Uid { get; set; }
        [ConcurrencyCheck]
        public Guid Timestamp { get; set; }
    }

    public sealed class Plain
    {
        public Guid Uid { get; set; }
    }

    public sealed class CatalogHost : SchemataTenantHost;

    /// <summary>Registration-only repository stand-in; the builder never resolves it.</summary>
    private sealed class StubRepository<TEntity> : IRepository<TEntity>
        where TEntity : class
    {
        public AdviceContext AdviceContext => throw new NotSupportedException();
        public IUnitOfWork Begin() => throw new NotSupportedException();
        public void Join(IUnitOfWork uow) => throw new NotSupportedException();
        public Task CommitAsync(System.Threading.CancellationToken ct = default) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => default;
        public IAsyncEnumerable<TResult> ListAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<TEntity?> GetAsync(TEntity? entity, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<TResult?> GetAsync<TResult>(TEntity? entity, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<OriginalResult<TResult>> GetOriginalAsync<TResult>(TEntity entity, System.Linq.Expressions.Expression<Func<TEntity, TResult>> projection, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<TEntity?> FindAsync(object[] keys, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<TResult?> FindAsync<TResult>(object[] keys, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<TResult?> FirstOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<TResult?> SingleOrDefaultAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<bool> AnyAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<int> CountAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public ValueTask<long> LongCountAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, System.Threading.CancellationToken ct = default, QueryOptions options = default) => throw new NotSupportedException();
        public Task<MutationResult> AddAsync(TEntity entity, System.Threading.CancellationToken ct = default, MutationOptions options = default) => throw new NotSupportedException();
        public Task AddRangeAsync(IEnumerable<TEntity> entities, System.Threading.CancellationToken ct = default, MutationOptions options = default) => throw new NotSupportedException();
        public Task<MutationResult> UpdateAsync(TEntity entity, System.Threading.CancellationToken ct = default, MutationOptions options = default) => throw new NotSupportedException();
        public Task<MutationResult> RemoveAsync(TEntity entity, System.Threading.CancellationToken ct = default, MutationOptions options = default) => throw new NotSupportedException();
        public Task RemoveRangeAsync(IEnumerable<TEntity> entities, System.Threading.CancellationToken ct = default, MutationOptions options = default) => throw new NotSupportedException();
    }
}
