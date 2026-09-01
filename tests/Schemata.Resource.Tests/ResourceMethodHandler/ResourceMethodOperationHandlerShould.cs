using System;
using System.Collections.Generic;
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
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Handlers;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class ResourceMethodOperationHandlerShould
{
    [Fact]
    public async Task CollectionMethod_Dispatches_Request_Without_Loading_Entity() {
        var repository = Mock.Of<IRepository<MethodEntity>>(MockBehavior.Strict);
        var handler    = new MethodHandler();
        using var services = Services(repository, handler: handler);
        var operation = services.GetRequiredService<ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var request   = new MethodRequest();
        using var cts = new CancellationTokenSource();

        var response = await operation.InvokeAsync("batchArchive", null, request, principal, cts.Token);

        Assert.Same(handler.Response, response);
        Assert.Equal(1, handler.Invocations);
        Assert.Same(request, handler.Request);
        Assert.Same(principal, handler.Request!.Principal);
        Assert.Equal(cts.Token, handler.Ct);
    }

    [Fact]
    public async Task CollectionMethod_Runs_Resource_And_Command_Advisor_Chains() {
        var calls           = new List<string>();
        var resourceAdvisor = new RecordingResourceAdvisor(calls);
        var commandAdvisor  = new RecordingCommandAdvisor(calls);
        var handler         = new MethodHandler(calls);
        using var services = Services(
            Mock.Of<IRepository<MethodEntity>>(MockBehavior.Strict),
            handler,
            services => {
                services.AddSingleton<IResourceMethodRequestAdvisor<MethodEntity, MethodRequest>>(resourceAdvisor);
                services.AddSingleton<IRequestPipelineAdvisor<MethodRequest, MethodResponse>>(commandAdvisor);
            });
        var operation = services.GetRequiredService<ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var request   = new MethodRequest();

        var response = await operation.InvokeAsync("batchArchive", null, request, principal, default);

        Assert.Equal(["resource", "command", "handler"], calls);
        Assert.Same(principal, commandAdvisor.Principal);
        Assert.Same(request, handler.Request);
        Assert.Same(principal, handler.Request!.Principal);
        Assert.Same(handler.Response, response);
    }

    [Fact]
    public async Task InstanceMethod_Uses_Route_Target_Over_Request_Payload() {
        var entity = new MethodEntity { CanonicalName = "methodEntities/route" };
        var repository = new Mock<IRepository<MethodEntity>>();
        repository.Setup(value => value.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(value => value.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<MethodEntity>, IQueryable<MethodEntity>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<MethodEntity?>(entity));
        var handler = new MethodHandler();
        using var services = Services(repository.Object, handler: handler);
        var operation = services.GetRequiredService<ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>>();
        var request = new MethodRequest { CanonicalName = "methodEntities/payload" };

        await operation.InvokeAsync("archive", entity.CanonicalName, request, null, default);

        Assert.Equal(entity.CanonicalName, handler.Request!.CanonicalName);
    }

    private static ServiceProvider Services(
        IRepository<MethodEntity> repository,
        MethodHandler?            handler   = null,
        Action<ServiceCollection>? configure = null
    ) {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton<IRequestHandler<MethodRequest, MethodResponse>>(handler ?? new MethodHandler());
        services.AddSingleton(sp => new ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>(
            sp.GetRequiredService<IRepository<MethodEntity>>(), sp, new InProcessRequestDispatcher(sp)));
        services.AddSingleton<
            IRequestHandler<ResourceMethodRequest<MethodEntity, MethodRequest, MethodResponse>, MethodResponse>,
            ResourceMethodDispatchHandler<MethodEntity, MethodRequest, MethodResponse>>();
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [CanonicalName("methodEntities/{methodEntity}")]
    public sealed class MethodEntity : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    public sealed class MethodRequest : ICanonicalName, ICommand<MethodResponse>, IRequestPrincipal
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
        public ClaimsPrincipal? Principal { get; set; }
    }


    private sealed class RecordingResourceAdvisor(List<string> calls)
        : IResourceMethodRequestAdvisor<MethodEntity, MethodRequest>
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext                         ctx,
            MethodRequest                         request,
            ResourceRequestContainer<MethodEntity> container,
            ClaimsPrincipal?                      principal,
            CancellationToken                     ct = default
        ) {
            calls.Add("resource");
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class RecordingCommandAdvisor(List<string> calls) : IRequestPipelineAdvisor<MethodRequest, MethodResponse>
    {
        public int Order => 0;

        public ClaimsPrincipal? Principal { get; private set; }

        public Task<MethodResponse> AdviseAsync(
            AdviceContext                             ctx,
            MethodRequest                             request,
            RequestHandlerContinuation<MethodResponse> next,
            CancellationToken                         ct = default
        ) {
            calls.Add("command");
            Principal = request.Principal;
            return next(ct);
        }
    }

    private sealed class MethodHandler(List<string>? calls = null) : IRequestHandler<MethodRequest, MethodResponse>
    {
        public int Invocations { get; private set; }

        public MethodRequest? Request { get; private set; }

        public MethodResponse Response { get; } = new();

        public CancellationToken Ct { get; private set; }

        public Task<MethodResponse> HandleAsync(MethodRequest request, CancellationToken ct = default) {
            Invocations++;
            calls?.Add("handler");
            Request = request;
            Ct      = ct;
            return Task.FromResult(Response);
        }
    }

    public sealed class MethodResponse : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

}
