using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Actor.Foundation.Handlers;
using Schemata.Actor.Skeleton;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class ActorSerializingHandlerShould
{
    [Fact]
    public async Task NullRouting_UsesTheKeyedDefaultHandler() {
        var request = new TestRequest();
        var routing = new Mock<IActorRouting<TestRequest>>(MockBehavior.Strict);
        routing.Setup(value => value.ResolveAsync(request, It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((ActorId?)null);
        var defaultHandler = new Mock<IRequestHandler<TestRequest, string>>(MockBehavior.Strict);
        defaultHandler.Setup(value => value.HandleAsync(request, It.IsAny<AdviceContext>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync("default");
        var services = new ServiceCollection();
        services.AddKeyedSingleton("default", defaultHandler.Object);
        await using var provider = services.BuildServiceProvider();
        var handler = new ActorSerializingHandler<TestRequest, string>(
            Mock.Of<IActorSystem>(), provider, routing.Object, "default");

        var result = await handler.HandleAsync(request, new(provider), CancellationToken.None);

        Assert.Equal("default", result);
        routing.VerifyAll();
        defaultHandler.VerifyAll();
    }

    [Fact]
    public async Task ActorRouting_UsesTheResolvedActor() {
        var request = new TestRequest();
        var actorId = new ActorId("test", "one");
        var routing = new Mock<IActorRouting<TestRequest>>(MockBehavior.Strict);
        routing.Setup(value => value.ResolveAsync(request, It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(actorId);
        var actor = new Mock<IActorRef>(MockBehavior.Strict);
        actor.Setup(value => value.AskAsync<TestRequest, string>(
                         request, It.IsAny<MessageContext?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync("actor");
        var actors = new Mock<IActorSystem>(MockBehavior.Strict);
        actors.Setup(value => value.GetAsync(actorId)).ReturnsAsync(actor.Object);
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        var handler = new ActorSerializingHandler<TestRequest, string>(
            actors.Object, provider, routing.Object, "default");

        var result = await handler.HandleAsync(request, new(provider), CancellationToken.None);

        Assert.Equal("actor", result);
        routing.VerifyAll();
        actors.VerifyAll();
        actor.VerifyAll();
    }

    public sealed class TestRequest : IRequest<string>;
}
