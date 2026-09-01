using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceDetailResponsePipelineAdvisorShould
{
    private static readonly Guid Timestamp = Guid.Parse("0f8fad5b-d9cb-469f-a666-085b969149fb");

    private static string WeakTag(Guid timestamp) {
        return $"W/\"{
            Convert.ToBase64String(timestamp.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        }\"";
    }

    [Fact]
    public async Task Create_Dispatch_DerivesParent_And_SetsWeakETag() {
        var detail = MappedDetail();

        using var services = BuildServices<Detail>(CreateRepository(), CreateMapper(detail));
        var result = await DispatchCreateAsync(services);

        Assert.Equal("tenants/t1", result.Detail!.Parent);
        Assert.Equal(WeakTag(Timestamp), result.Detail.EntityTag);
    }

    [Fact]
    public async Task Get_Dispatch_DerivesParent_And_SetsWeakETag() {
        var detail = MappedDetail();
        detail.Parent = "stale/parent";

        using var services = BuildServices<Detail>(CreateRepository(), CreateMapper(detail));
        var result = await DispatchGetAsync<Detail>(services);

        // Derivation overrides whatever the mapping left behind.
        Assert.Equal("tenants/t1", result.Detail!.Parent);
        Assert.Equal(WeakTag(Timestamp), result.Detail.EntityTag);
    }

    [Fact]
    public async Task Update_Dispatch_DerivesParent_And_SetsWeakETag() {
        var detail = MappedDetail();

        using var services = BuildServices<Detail>(CreateRepository(), CreateMapper(detail));
        var result = await DispatchUpdateAsync(services);

        Assert.Equal("tenants/t1", result.Detail!.Parent);
        Assert.Equal(WeakTag(Timestamp), result.Detail.EntityTag);
    }

    [Fact]
    public async Task Get_EmptyTimestamp_LeavesEntityTag_Unset() {
        var detail = MappedDetail();
        detail.Timestamp = Guid.Empty;

        using var services = BuildServices<Detail>(CreateRepository(), CreateMapper(detail));
        var result = await DispatchGetAsync<Detail>(services);

        Assert.Equal("tenants/t1", result.Detail!.Parent);
        Assert.Null(result.Detail.EntityTag);
    }

    [Fact]
    public async Task Get_SuppressedFreshness_LeavesEntityTag_Unset() {
        var detail = MappedDetail();

        using var services = BuildServices<Detail>(CreateRepository(), CreateMapper(detail),
            options: new SchemataResourceOptions { SuppressFreshness = true });
        var result = await DispatchGetAsync<Detail>(services);

        Assert.Equal("tenants/t1", result.Detail!.Parent);
        Assert.Null(result.Detail.EntityTag);
    }

    [Fact]
    public async Task Get_CustomEntityTagProvider_OverridesDefaultTag() {
        var detail = MappedDetail();

        using var services = BuildServices<Detail>(CreateRepository(), CreateMapper(detail), services => {
            // The last IEntityTagProvider registration wins.
            services.AddSingleton<IEntityTagProvider>(new FixedEntityTagProvider("W/\"custom\""));
        });
        var result = await DispatchGetAsync<Detail>(services);

        Assert.Equal("tenants/t1", result.Detail!.Parent);
        Assert.Equal("W/\"custom\"", result.Detail.EntityTag);
    }

    [Fact]
    public async Task Get_PlainDetail_ReturnsContinuationDetail() {
        var detail = new PlainDetail { CanonicalName = "tenants/t1/hosts/h1" };

        using var services = BuildServices<PlainDetail>(CreateRepository(), CreatePlainMapper(detail));
        var result = await DispatchGetAsync<PlainDetail>(services);

        Assert.Same(detail, result.Detail);
    }

    [Fact]
    public async Task Get_NullDetail_ReturnsContinuationResponse() {
        var advisor  = new ResourceGetResponsePipelineAdvisor<Entity, Detail>(new DefaultEntityTagProvider());
        var ctx      = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var response = new GetResultBase<Detail> { Detail = null };
        var calls    = 0;

        var result = await advisor.AdviseAsync(
            ctx, new(new GetRequest(), null), _ => {
                calls++;
                return Task.FromResult(response);
            }, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Same(response, result);
        Assert.Null(result.Detail);
    }

    [Theory]
    [InlineData(null,                                    null)]
    [InlineData("",                                      null)]
    [InlineData("tenants/t1",                            null)]
    [InlineData("tenants/t1/hosts/h1",                   "tenants/t1")]
    [InlineData("organizations/o/projects/p/datasets/d", "organizations/o/projects/p")]
    [InlineData("organizations/o/projects/p/datasets",   null)]
    public async Task DerivedParent_Matches_StripLastTwoSegments(string? canonical, string? expected) {
        var detail = new Detail { CanonicalName = canonical };

        var advisor  = new ResourceGetResponsePipelineAdvisor<Entity, Detail>(new DefaultEntityTagProvider());
        var ctx      = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var response = new GetResultBase<Detail> { Detail = detail };

        await advisor.AdviseAsync(ctx, new(new GetRequest(), null), _ => Task.FromResult(response), CancellationToken.None);

        Assert.Equal(expected, detail.Parent);
    }

    [Fact]
    public void Orders_Anchor_Above_ListWrap_And_Idempotency() {
        Assert.Equal(SecurityOrders.ResponseFamily + 10_000_000,
            new ResourceGetResponsePipelineAdvisor<Entity, Detail>(new DefaultEntityTagProvider()).Order);

        // The dispatcher composes the wrap in ascending Order, so after segments run in reverse:
        // staying above SecurityOrders.Idempotency keeps an idempotency wrap's commit behind the
        // shaping, and above ResponseFamily keeps the detail wrap behind the list wrap.
        Assert.True(ResourceDetailResponsePipelineAdvisor.DefaultOrder > SecurityOrders.Idempotency);
        Assert.True(ResourceDetailResponsePipelineAdvisor.DefaultOrder
            > new ResourceListResponsePipelineAdvisor<Entity, Summary>().Order);
    }

    [Fact]
    public void AddResource_Registers_ClosedDetailAdvisors_ForAllVerbs() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddResource(new ResourceAttribute<Entity, Request, Detail, Summary>(), registry);

        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestPipelineAdvisor<GetResourceQueryRequest<Entity, Detail>, GetResultBase<Detail>>)
            && service.ImplementationType == typeof(ResourceGetResponsePipelineAdvisor<Entity, Detail>)
            && service.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>)
            && service.ImplementationType == typeof(ResourceCreateResponsePipelineAdvisor<Entity, Request, Detail>)
            && service.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>)
            && service.ImplementationType == typeof(ResourceUpdateResponsePipelineAdvisor<Entity, Request, Detail>)
            && service.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestPipelineAdvisor<DeleteResourceRequest<Entity, Detail>, DeleteResultBase<Detail>>)
            && service.ImplementationType == typeof(ResourceDeleteResponsePipelineAdvisor<Entity, Detail>)
            && service.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSchemataResources_TryAdds_DefaultEntityTagProvider() {
        var services = new ServiceCollection();

        services.AddSchemataResources();

        Assert.Contains(services, service =>
            service.ServiceType == typeof(IEntityTagProvider)
            && service.ImplementationType == typeof(DefaultEntityTagProvider));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DefaultEntityTagProvider>(provider.GetRequiredService<IEntityTagProvider>());
    }

    #region Dispatch helpers

    private static Task<CreateResultBase<Detail>> DispatchCreateAsync(ServiceProvider services) {
        var dispatcher = new InProcessRequestDispatcher(services);
        return dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
            new(new Request(), null), CancellationToken.None);
    }

    private static Task<GetResultBase<TGetDetail>> DispatchGetAsync<TGetDetail>(ServiceProvider services)
        where TGetDetail : class, ICanonicalName {
        var dispatcher = new InProcessRequestDispatcher(services);
        return dispatcher.SendAsync<GetResourceQueryRequest<Entity, TGetDetail>, GetResultBase<TGetDetail>>(
            new(new GetRequest { CanonicalName = "entities/e1" }, null), CancellationToken.None);
    }

    private static Task<UpdateResultBase<Detail>> DispatchUpdateAsync(ServiceProvider services) {
        var dispatcher = new InProcessRequestDispatcher(services);
        return dispatcher.SendAsync<UpdateResourceRequest<Entity, Request, Detail>, UpdateResultBase<Detail>>(
            new("entities/e1", new Request(), null), CancellationToken.None);
    }

    private static Detail MappedDetail() {
        return new Detail {
            CanonicalName = "tenants/t1/hosts/h1",
            Timestamp     = Timestamp,
        };
    }

    private static Mock<IRepository<Entity>> CreateRepository() {
        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.SuppressQuerySoftDelete())
                  .Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                          It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                          It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<Entity?>(new Entity { Name = "e1", CanonicalName = "entities/e1" }));

        return repository;
    }

    private static Mock<ISimpleMapper> CreateMapper(Detail detail) {
        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>()))
              .Returns(new Entity { Name = "e1", CanonicalName = "entities/e1" });
        mapper.Setup(m => m.Map<Entity, Detail>(It.IsAny<Entity>()))
              .Returns(detail);

        return mapper;
    }

    private static Mock<ISimpleMapper> CreatePlainMapper(PlainDetail detail) {
        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Entity, PlainDetail>(It.IsAny<Entity>()))
              .Returns(detail);

        return mapper;
    }

    private static ServiceProvider BuildServices<TDetailView>(
        Mock<IRepository<Entity>>     repository,
        Mock<ISimpleMapper>           mapper,
        Action<ServiceCollection>?    configure = null,
        SchemataResourceOptions?      options   = null
    )
        where TDetailView : class, ICanonicalName {
        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(mapper.Object);
        services.AddSingleton<
            IRequestHandler<GetResourceQueryRequest<Entity, TDetailView>, GetResultBase<TDetailView>>,
            DefaultGetResourceHandler<Entity, Request, TDetailView, Summary>>();
        services.AddSingleton<
            IRequestHandler<CreateResourceRequest<Entity, Request, TDetailView>, CreateResultBase<TDetailView>>,
            DefaultCreateResourceHandler<Entity, Request, TDetailView, Summary>>();
        services.AddSingleton<
            IRequestHandler<UpdateResourceRequest<Entity, Request, TDetailView>, UpdateResultBase<TDetailView>>,
            DefaultUpdateResourceHandler<Entity, Request, TDetailView, Summary>>();
        services.AddSingleton<ResourceOperationHandler<Entity, Request, TDetailView, Summary>>();
        services.AddSingleton<IEntityTagProvider, DefaultEntityTagProvider>();
        services.AddSingleton<
            IRequestPipelineAdvisor<GetResourceQueryRequest<Entity, TDetailView>, GetResultBase<TDetailView>>,
            ResourceGetResponsePipelineAdvisor<Entity, TDetailView>>();
        services.AddSingleton<
            IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, TDetailView>, CreateResultBase<TDetailView>>,
            ResourceCreateResponsePipelineAdvisor<Entity, Request, TDetailView>>();
        services.AddSingleton<
            IRequestPipelineAdvisor<UpdateResourceRequest<Entity, Request, TDetailView>, UpdateResultBase<TDetailView>>,
            ResourceUpdateResponsePipelineAdvisor<Entity, Request, TDetailView>>();
        configure?.Invoke(services);

        if (options is not null) {
            services.AddSingleton<IOptions<SchemataResourceOptions>>(Options.Create(options));
        }

        return services.BuildServiceProvider();
    }

    #endregion

    private sealed class FixedEntityTagProvider(string tag) : IEntityTagProvider
    {
        #region IEntityTagProvider Members

        public string? GetEntityTag<TEntity, TDetail>(TDetail? detail, AdviceContext ctx)
            where TEntity : class, ICanonicalName
            where TDetail : class, ICanonicalName {
            return tag;
        }

        #endregion
    }

    #region Fixtures

    [CanonicalName("entities/{entity}")]
    public sealed class Entity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Request : ICanonicalName
    {
        public string? DisplayName { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Detail : ICanonicalName, IChild, IFreshness, IConcurrency
    {
        public string? Parent { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        public string? EntityTag { get; set; }

        public Guid Timestamp { get; set; }
    }

    public sealed class PlainDetail : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Summary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion
}
