using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
using Schemata.Resource.Foundation.Models;
using Schemata.Resource.Tests.Fixtures;
using Xunit;
using TestEntity = Schemata.Resource.Tests.ResourceRequestPipelineAdvisorShould.Entity;
using TestRequest = Schemata.Resource.Tests.ResourceRequestPipelineAdvisorShould.Request;
using TestDetail = Schemata.Resource.Tests.ResourceRequestPipelineAdvisorShould.Detail;
using TestSummary = Schemata.Resource.Tests.ResourceRequestPipelineAdvisorShould.Summary;

namespace Schemata.Resource.Tests;

/// <summary>
///     One dispatch constructs one <see cref="AdviceContext" /> and hands that same instance to the
///     request pipeline advisor, the handler, and the resource advisor stages.
/// </summary>
public class ResourceAdviceContextShould
{
    [Fact]
    public async Task Dispatch_PassesOneContext_ToThePipelineAdvisor_TheHandler_AndResourceAdvisors() {
        var pipeline  = new CapturingPipelineAdvisor();
        var resource  = new CapturingResourceAdvisor();
        using var services = BuildServices(pipeline, resource);
        var handler = (ContextCapturingHandler)services.GetRequiredService<
            IRequestHandler<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>>();
        var dispatcher = new InProcessRequestDispatcher(services);

        await dispatcher.SendAsync<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>(
            new(new() { DisplayName = "created" }, null), CancellationToken.None);

        Assert.NotNull(pipeline.Context);
        Assert.Same(pipeline.Context, handler.Context);
        Assert.Same(handler.Context, resource.Context);
    }

    [Fact]
    public async Task ParallelDispatches_RunOnDistinctContexts() {
        var pipeline = new ConcurrentCapturingPipelineAdvisor();

        using var services = BuildServices(pipeline, new CapturingResourceAdvisor());
        var dispatcher = new InProcessRequestDispatcher(services);

        await Task.WhenAll(
            dispatcher.SendAsync<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>(
                new(new() { DisplayName = "first" }, null), CancellationToken.None),
            dispatcher.SendAsync<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>(
                new(new() { DisplayName = "second" }, null), CancellationToken.None));

        var contexts = pipeline.Contexts.ToArray();
        Assert.Equal(2, contexts.Length);
        Assert.NotSame(contexts[0], contexts[1]);
    }

    private static ServiceProvider BuildServices(
        IRequestPipelineAdvisor<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>> pipeline,
        IResourceCreateRequestAdvisor<TestEntity, TestRequest, TestDetail>       resource
    ) {
        var entity = new TestEntity { Name = "e1" };
        var detail = new TestDetail { Name = "e1" };

        var repository = new Mock<IRepository<TestEntity>>();
        repository.Setup(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<TestEntity>, IQueryable<TestEntity>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(new ValueTask<TestEntity?>(entity));

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<TestRequest, TestEntity>(It.IsAny<TestRequest>())).Returns(entity);
        mapper.Setup(m => m.Map<TestEntity, TestDetail>(It.IsAny<TestEntity>())).Returns(detail);

        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton(mapper.Object);
        services.AddSingleton(ResourceMutationMock.Create<TestEntity>().Object);
        services.AddSingleton<ResourceOperationHandler<TestEntity, TestRequest, TestDetail, TestSummary>>();
        services.AddSingleton<IResourceCreateRequestAdvisor<TestEntity, TestRequest, TestDetail>>(resource);
        services.AddSingleton(pipeline);
        services.AddSingleton<IRequestHandler<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>>(
            provider => new ContextCapturingHandler(
                new DefaultCreateResourceHandler<TestEntity, TestRequest, TestDetail, TestSummary>(
                    provider.GetRequiredService<ResourceOperationHandler<TestEntity, TestRequest, TestDetail, TestSummary>>())));

        return services.BuildServiceProvider();
    }

    private sealed class ContextCapturingHandler(IRequestHandler<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>> inner)
        : IRequestHandler<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>
    {
        public AdviceContext? Context { get; private set; }

        public Task<CreateResultBase<TestDetail>> HandleAsync(
            CreateResourceRequest<TestEntity, TestRequest, TestDetail> request,
            AdviceContext                                              context,
            CancellationToken                                          ct = default
        ) {
            Context = context;

            return inner.HandleAsync(request, context, ct);
        }
    }

    private sealed class CapturingPipelineAdvisor : IRequestPipelineAdvisor<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>
    {
        public AdviceContext? Context { get; private set; }

        public int Order => 0;

        public Task<CreateResultBase<TestDetail>> AdviseAsync(
            AdviceContext                                        ctx,
            CreateResourceRequest<TestEntity, TestRequest, TestDetail>       request,
            RequestHandlerContinuation<CreateResultBase<TestDetail>> next,
            CancellationToken                                    ct = default
        ) {
            Context = ctx;

            return next(ct);
        }
    }

    private sealed class ConcurrentCapturingPipelineAdvisor : IRequestPipelineAdvisor<CreateResourceRequest<TestEntity, TestRequest, TestDetail>, CreateResultBase<TestDetail>>
    {
        public ConcurrentBag<AdviceContext> Contexts { get; } = [];

        public int Order => 0;

        public Task<CreateResultBase<TestDetail>> AdviseAsync(
            AdviceContext                                        ctx,
            CreateResourceRequest<TestEntity, TestRequest, TestDetail>       request,
            RequestHandlerContinuation<CreateResultBase<TestDetail>> next,
            CancellationToken                                    ct = default
        ) {
            Contexts.Add(ctx);

            return next(ct);
        }
    }

    private sealed class CapturingResourceAdvisor : IResourceCreateRequestAdvisor<TestEntity, TestRequest, TestDetail>
    {
        public AdviceContext? Context { get; private set; }

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                             ctx,
            TestRequest                                   request,
            ResourceRequestContainer<TestEntity>          container,
            ClaimsPrincipal?                          principal,
            HandledResponse<CreateResultBase<TestDetail>> output,
            CancellationToken                         ct = default
        ) {
            Context = ctx;

            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
