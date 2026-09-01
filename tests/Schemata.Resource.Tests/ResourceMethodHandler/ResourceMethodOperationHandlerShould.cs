using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class ResourceMethodOperationHandlerShould
{
    [Fact]
    public async Task CollectionMethod_Dispatches_Request_Without_Loading_Entity() {
        var repository = Mock.Of<Entity.Repository.IRepository<MethodEntity>>(MockBehavior.Strict);
        var dispatcher = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        dispatcher.Setup(d => d.SendAsync<MethodRequest, MethodResponse>(
                             It.IsAny<MethodRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new MethodResponse());
        using var services = Services();
        using var ambient = AdviceContext.Establish(new AdviceContext(services));
        var operation = new ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>(
            repository, services, dispatcher.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var request   = new MethodRequest();
        using var cts = new CancellationTokenSource();

        await operation.InvokeAsync("batchArchive", null, request, principal, cts.Token);

        dispatcher.Verify(d => d.SendAsync<MethodRequest, MethodResponse>(
                              It.Is<MethodRequest>(sent =>
                                  ReferenceEquals(sent, request) && ReferenceEquals(sent.Principal, principal)),
                              cts.Token), Times.Once);
    }

    [Fact]
    public async Task CollectionMethod_Runs_Resource_And_Command_Advisor_Chains() {
        var calls           = new List<string>();
        var resourceAdvisor = new RecordingResourceAdvisor(calls);
        var commandAdvisor  = new RecordingCommandAdvisor(calls);
        var handler         = new MethodHandler(calls);
        var services = new ServiceCollection()
                      .AddSingleton<IResourceMethodRequestAdvisor<MethodEntity, MethodRequest>>(resourceAdvisor)
                      .AddSingleton<ICommandAdvisor<MethodRequest>>(commandAdvisor)
                      .AddSingleton<IRequestHandler<MethodRequest, MethodResponse>>(handler)
                      .BuildServiceProvider();
        var dispatcher = new InProcessRequestDispatcher(services);
        var operation = new ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>(
            Mock.Of<Entity.Repository.IRepository<MethodEntity>>(MockBehavior.Strict),
            services,
            dispatcher);
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
        var repository = new Mock<Entity.Repository.IRepository<MethodEntity>>();
        repository.Setup(value => value.SuppressQuerySoftDelete()).Returns(Mock.Of<System.IDisposable>());
        repository.Setup(value => value.SingleOrDefaultAsync(
                             It.IsAny<System.Func<System.Linq.IQueryable<MethodEntity>,
                                 System.Linq.IQueryable<MethodEntity>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<MethodEntity?>(entity));
        var dispatcher = new Mock<IRequestDispatcher>();
        dispatcher.Setup(value => value.SendAsync<MethodRequest, MethodResponse>(
                             It.IsAny<MethodRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new MethodResponse());
        using var services = Services();
        var operation = new ResourceMethodOperationHandler<MethodEntity, MethodRequest, MethodResponse>(
            repository.Object, services, dispatcher.Object);
        var request = new MethodRequest { CanonicalName = "methodEntities/payload" };

        await operation.InvokeAsync("archive", entity.CanonicalName, request, null, default);

        dispatcher.Verify(value => value.SendAsync<MethodRequest, MethodResponse>(
                              It.Is<MethodRequest>(sent => sent.CanonicalName == entity.CanonicalName),
                              It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ServiceProvider Services() {
        return new ServiceCollection().BuildServiceProvider();
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

        public bool Invoked { get; private set; }

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

    private sealed class RecordingCommandAdvisor(List<string> calls) : ICommandAdvisor<MethodRequest>
    {
        public int Order => 0;

        public ClaimsPrincipal? Principal { get; private set; }

        public Task<AdviseResult> AdviseAsync(
            AdviceContext    ctx,
            MethodRequest    request,
            CancellationToken ct = default
        ) {
            calls.Add("command");
            Principal = request.Principal;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class MethodHandler(List<string> calls) : IRequestHandler<MethodRequest, MethodResponse>
    {
        public MethodRequest? Request { get; private set; }

        public MethodResponse Response { get; } = new();

        public Task<MethodResponse> HandleAsync(MethodRequest request, CancellationToken ct = default) {
            calls.Add("handler");
            Request = request;
            return Task.FromResult(Response);
        }
    }

    public sealed class MethodResponse : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

}
