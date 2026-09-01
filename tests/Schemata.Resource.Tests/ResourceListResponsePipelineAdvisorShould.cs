using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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

public class ResourceListResponsePipelineAdvisorShould
{
    [Fact]
    public async Task List_PopulatedResult_DerivesParent_OnResponseSummaries() {
        var rows = new[] {
            new Entity { Name = "e1", CanonicalName = "entities/e1" },
            new Entity { Name = "e2", CanonicalName = "entities/e2" },
            new Entity { Name = "e3", CanonicalName = "entities/e3" },
        };
        var canonicals = new Dictionary<string, string?> {
            ["e1"] = "tenants/t1/hosts/h1",
            ["e2"] = "tenants/t2/hosts/h2",
            ["e3"] = "tenants/t3",
        };

        var mapped = new List<Summary>();
        var (repository, mapper) = CreateDoubles(rows);
        mapper.Setup(m => m.Map<Entity, Summary>(It.IsAny<Entity>()))
              .Returns((Entity e) => {
                   var summary = new Summary { CanonicalName = canonicals[e.Name!] };
                   mapped.Add(summary);
                   return summary;
               });

        using var services = BuildServices<Summary>(repository.Object, mapper.Object);
        var dispatcher = new InProcessRequestDispatcher(services);

        var result = await dispatcher.SendAsync<ListResourceQueryRequest<Entity, Summary>, ListResultBase<Summary>>(
            new(new ListRequest(), null), CancellationToken.None);

        Assert.NotNull(result.Entities);
        Assert.Equal(3, result.Entities!.Count);
        Assert.Equal(3, result.TotalSize);
        Assert.Null(result.NextPageToken);

        Assert.Equal("tenants/t1", result.Entities[0].Parent);
        Assert.Equal("tenants/t2", result.Entities[1].Parent);
        Assert.Null(result.Entities[2].Parent);

        // The wrap mutates the elements the handler assembled: same summary instances, in place.
        Assert.Equal(mapped, result.Entities);
    }

    [Fact]
    public async Task List_EmptyResult_ReturnsResponseUnchanged() {
        var (repository, mapper) = CreateDoubles([]);
        mapper.Setup(m => m.Map<Entity, Summary>(It.IsAny<Entity>())).Returns(new Summary());

        using var services = BuildServices<Summary>(repository.Object, mapper.Object);
        var dispatcher = new InProcessRequestDispatcher(services);

        var result = await dispatcher.SendAsync<ListResourceQueryRequest<Entity, Summary>, ListResultBase<Summary>>(
            new(new ListRequest(), null), CancellationToken.None);

        Assert.NotNull(result.Entities);
        Assert.Empty(result.Entities!);
        Assert.Equal(0, result.TotalSize);
        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task List_NonChildSummary_PassesThrough() {
        var rows = new[] {
            new Entity { Name = "e1", CanonicalName = "entities/e1" },
        };

        var mapped = new List<PlainSummary>();
        var (repository, mapper) = CreateDoubles(rows);
        mapper.Setup(m => m.Map<Entity, PlainSummary>(It.IsAny<Entity>()))
              .Returns((Entity e) => {
                   var summary = new PlainSummary { CanonicalName = "tenants/t1/hosts/h1" };
                   mapped.Add(summary);
                   return summary;
               });

        using var services = BuildServices<PlainSummary>(repository.Object, mapper.Object);
        var dispatcher = new InProcessRequestDispatcher(services);

        var result = await dispatcher.SendAsync<ListResourceQueryRequest<Entity, PlainSummary>, ListResultBase<PlainSummary>>(
            new(new ListRequest(), null), CancellationToken.None);

        Assert.NotNull(result.Entities);
        Assert.Equal(mapped, result.Entities!);
    }

    [Fact]
    public async Task NullEntities_ReturnsContinuationResponse() {
        var advisor  = new ResourceListResponsePipelineAdvisor<Entity, Summary>();
        var ctx      = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var envelope = new ListResourceQueryRequest<Entity, Summary>(new ListRequest(), null);
        var response = new ListResultBase<Summary> { Entities = null, TotalSize = 5 };
        var calls    = 0;

        var result = await advisor.AdviseAsync(ctx, envelope, _ => {
            calls++;
            return Task.FromResult(response);
        }, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Same(response, result);
        Assert.Null(result.Entities);
    }

    [Fact]
    public void Order_Anchors_ResponseFamily() {
        Assert.Equal(SecurityOrders.ResponseFamily, new ResourceListResponsePipelineAdvisor<Entity, Summary>().Order);
    }

    [Fact]
    public void AddResource_Registers_ClosedAdvisor_PerResource() {
        var services = new ServiceCollection();
        var registry = new ResourceRegistry();

        services.AddResource(new ResourceAttribute<Entity, Request, Detail, Summary>(), registry);

        Assert.Contains(services, service =>
            service.ServiceType
         == typeof(IRequestPipelineAdvisor<ListResourceQueryRequest<Entity, Summary>, ListResultBase<Summary>>)
         && service.ImplementationType == typeof(ResourceListResponsePipelineAdvisor<Entity, Summary>)
         && service.Lifetime == ServiceLifetime.Scoped);
    }

    private static (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper) CreateDoubles(Entity[] rows) {
        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<Entity>, IQueryable<Entity>> query, CancellationToken _) =>
                      ToAsyncEnumerable(query(rows.AsQueryable())));
        repository.Setup(r => r.CountAsync(
                              It.IsAny<Func<IQueryable<Entity>, IQueryable<Entity>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<Entity>, IQueryable<Entity>> query, CancellationToken _) =>
                      new ValueTask<int>(query(rows.AsQueryable()).Count()));

        return (repository, new Mock<ISimpleMapper>());
    }

    private static ServiceProvider BuildServices<TSummary>(
        IRepository<Entity> repository,
        ISimpleMapper       mapper
    )
        where TSummary : class, ICanonicalName {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(mapper);
        services.AddDataProtection();
        services.AddSingleton<ResourceOperationHandler<Entity, Request, Detail, TSummary>>();
        services.AddSingleton<
            IRequestHandler<ListResourceQueryRequest<Entity, TSummary>, ListResultBase<TSummary>>,
            DefaultListResourceHandler<Entity, Request, Detail, TSummary>>();
        services.AddSingleton<
            IRequestPipelineAdvisor<ListResourceQueryRequest<Entity, TSummary>, ListResultBase<TSummary>>>(
            new ResourceListResponsePipelineAdvisor<Entity, TSummary>());

        return services.BuildServiceProvider();
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source) {
        foreach (var item in source) {
            yield return item;
            await Task.Yield();
        }
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
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Detail : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class Summary : ICanonicalName, IChild
    {
        public string? Parent { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class PlainSummary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    #endregion
}
