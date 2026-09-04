using Schemata.Core.Building;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceAdviceContextShould
{
    [Fact]
    public void Throw_When_No_Ambient_Context() {
        var services = new ServiceCollection().BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => ResourceAdviceContext.Create(services));

        Assert.Contains("dispatcher", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expose_Dispatcher_Items_To_Resource_Advisors() {
        // Facade entry: some other pipeline root already established the ambient context and a
        // command advisor stashed the marker before the resource handler ran.
        var (facadeRepository, facadeMapper) = CreatePipelineDoubles();
        var facadeSpy = new SpyCreateRequestAdvisor();
        using var facadeServices = BuildFacadeServices(facadeSpy, facadeRepository.Object, facadeMapper.Object);
        var facadeHandler = new ResourceOperationHandler<Entity, Request, Detail, Summary>(
            facadeServices, facadeRepository.Object, facadeMapper.Object);

        using (AdviceContext.Establish(new(facadeServices))) {
            AdviceContext.Current!.Set(new TestMarker());
            await facadeHandler.CreateAsync(new(), null, CancellationToken.None);
        }

        Assert.True(facadeSpy.SawMarker);

        // Dispatcher entry: the IRequestPipelineAdvisor<CreateResourceRequest<...>, CreateResultBase<Detail>> chain
        // stashes the marker during dispatch; the resource pipeline continues that same ambient context.
        var (dispatcherRepository, dispatcherMapper) = CreatePipelineDoubles();
        var dispatcherSpy = new SpyCreateRequestAdvisor();
        using var dispatcherServices = BuildDispatcherServices(
            dispatcherSpy, dispatcherRepository.Object, dispatcherMapper.Object);
        var dispatcher = new InProcessRequestDispatcher(dispatcherServices);

        await dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
            new(new(), null), CancellationToken.None);

        Assert.True(dispatcherSpy.SawMarker);
    }

    [Fact]
    public async Task Seed_Options_Suppression_Into_Ambient() {
        var options = new SchemataResourceOptions { SuppressFreshness = true };

        // Facade entry.
        var (facadeRepository, facadeMapper) = CreatePipelineDoubles();
        var facadeSpy = new SpyCreateRequestAdvisor();
        using var facadeServices = BuildFacadeServices(
            facadeSpy, facadeRepository.Object, facadeMapper.Object, options);
        var facadeHandler = new ResourceOperationHandler<Entity, Request, Detail, Summary>(
            facadeServices, facadeRepository.Object, facadeMapper.Object);

        using (AdviceContext.Establish(new(facadeServices))) {
            await facadeHandler.CreateAsync(new(), null, CancellationToken.None);
        }

        Assert.True(facadeSpy.SawSuppression);

        // Dispatcher entry.
        var (dispatcherRepository, dispatcherMapper) = CreatePipelineDoubles();
        var dispatcherSpy = new SpyCreateRequestAdvisor();
        using var dispatcherServices = BuildDispatcherServices(
            dispatcherSpy, dispatcherRepository.Object, dispatcherMapper.Object, options);
        var dispatcher = new InProcessRequestDispatcher(dispatcherServices);

        await dispatcher.SendAsync<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>(
            new(new(), null), CancellationToken.None);

        Assert.True(dispatcherSpy.SawSuppression);
    }

    private static (Mock<IRepository<Entity>> Repository, Mock<ISimpleMapper> Mapper) CreatePipelineDoubles() {
        var entity = new Entity { Name = "e1" };
        var detail = new Detail { Name = "e1" };

        var repository = new Mock<IRepository<Entity>>();
        repository.Setup(r => r.AddAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Request, Entity>(It.IsAny<Request>())).Returns(entity);
        mapper.Setup(m => m.Map<Entity, Detail>(It.IsAny<Entity>())).Returns(detail);

        return (repository, mapper);
    }

    private static ServiceProvider BuildFacadeServices(
        IResourceCreateRequestAdvisor<Entity, Request> spy,
        IRepository<Entity>                            repository,
        ISimpleMapper                                  mapper,
        SchemataResourceOptions?                       options = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(spy);
        services.AddSingleton(repository);
        services.AddSingleton(mapper);
        if (options is not null) {
            services.AddSingleton<IOptions<SchemataResourceOptions>>(Options.Create(options));
        }

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildDispatcherServices(
        IResourceCreateRequestAdvisor<Entity, Request> spy,
        IRepository<Entity>                            repository,
        ISimpleMapper                                  mapper,
        SchemataResourceOptions?                       options = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(spy);
        services.AddSingleton(repository);
        services.AddSingleton(mapper);
        services.AddSingleton<ResourceOperationHandler<Entity, Request, Detail, Summary>>();
        services.AddSingleton<
            IRequestHandler<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>,
            DefaultCreateResourceHandler<Entity, Request, Detail, Summary>>();
        services.AddSingleton<
            IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>, SetMarkerCommandAdvisor>();
        if (options is not null) {
            services.AddSingleton<IOptions<SchemataResourceOptions>>(Options.Create(options));
        }

        return services.BuildServiceProvider();
    }

    private sealed record TestMarker;

    /// <summary>Dispatcher-level command advisor that stashes <see cref="TestMarker" /> on the ambient context.</summary>
    private sealed class SetMarkerCommandAdvisor : IRequestPipelineAdvisor<CreateResourceRequest<Entity, Request, Detail>, CreateResultBase<Detail>>
    {
        public int Order => 0;

        public Task<CreateResultBase<Detail>> AdviseAsync(
            AdviceContext                                        ctx,
            CreateResourceRequest<Entity, Request, Detail>       a1,
            RequestHandlerContinuation<CreateResultBase<Detail>> next,
            CancellationToken                                    ct = default
        ) {
            ctx.Set(new TestMarker());
            return next(ct);
        }
    }

    /// <summary>Resource-pipeline observer recording what it sees on the ambient context it was handed.</summary>
    private sealed class SpyCreateRequestAdvisor : IResourceCreateRequestAdvisor<Entity, Request>
    {
        public int  Order          => 0;
        public bool SawMarker      { get; private set; }
        public bool SawSuppression { get; private set; }

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                    ctx,
            Request                          a1,
            ResourceRequestContainer<Entity> a2,
            ClaimsPrincipal?                 a3,
            CancellationToken                ct = default
        ) {
            SawMarker      = ctx.TryGet<TestMarker>(out _);
            SawSuppression = ctx.Has<FreshnessSuppressed>();
            return Task.FromResult(AdviseResult.Continue);
        }
    }

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

    public sealed class Summary : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
