using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Messaging.Skeleton.Tests.Fixtures;
using Xunit;

namespace Schemata.Messaging.Skeleton.Tests;

public class RequestDispatcherShould
{
    [Fact]
    public async Task Dispatch_ForACommand_RunsTheCommandAdvisorChain() {
        var trail   = new List<string>();
        var advisor = new TracingCommandAdvisor(0, "command-advisor", trail, AdviseResult.Continue);
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("hub");

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(advisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                                 .SendAsync<RenameWidget, string>(new RenameWidget("hub"));

        Assert.Equal("hub", result);
        Assert.Equal(["command-advisor"], trail);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_ForAQuery_RunsTheQueryAdvisorChain() {
        var trail   = new List<string>();
        var advisor = new TracingQueryAdvisor("query-advisor", trail);
        var handler = new Mock<IRequestHandler<CountWidgets, int>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<CountWidgets>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(7);

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<CountWidgets, int>>(handler.Object);
            services.AddSingleton<IQueryAdvisor<CountWidgets>>(advisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                                 .SendAsync<CountWidgets, int>(new CountWidgets());

        Assert.Equal(7, result);
        Assert.Equal(["query-advisor"], trail);
        handler.Verify(h => h.HandleAsync(It.IsAny<CountWidgets>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_ForAPlainRequest_RunsNoAdvisorChain() {
        // A plain IRequest<T> is neither ICommand nor IQuery<T>, so neither chain is even looked up
        // — registering advisors for it must have no effect.
        var commandAdvisor = new RecordingCommandAdvisorForPlainRequest();
        var queryAdvisor   = new RecordingQueryAdvisorForPlainRequest();
        var handler        = new Mock<IRequestHandler<PlainRequest, string>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<PlainRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("echo");

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<PlainRequest, string>>(handler.Object);
            services.AddSingleton<ICommandAdvisor<PlainRequest>>(commandAdvisor);
            services.AddSingleton<IQueryAdvisor<PlainRequest>>(queryAdvisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                                 .SendAsync<PlainRequest, string>(new PlainRequest("echo"));

        Assert.Equal("echo", result);
        Assert.False(commandAdvisor.Ran);
        Assert.False(queryAdvisor.Ran);
    }

    [Fact]
    public async Task Dispatch_WhenAnAdvisorHandlesAndSetsAResult_ShortCircuitsWithoutInvokingTheHandler() {
        var advisor = new HandlingCommandAdvisor("short-circuited");
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("hub");

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(advisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                                 .SendAsync<RenameWidget, string>(new RenameWidget("hub"));

        Assert.Equal("short-circuited", result);
        Assert.True(advisor.Ran);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_WhenAnAdvisorHandlesWithoutSettingAResult_Throws() {
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(new UnsetHandlingCommandAdvisor());
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new RenameWidget("hub")));

        Assert.Contains(typeof(RenameWidget).ToString(), error.Message);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_WhenAnAdvisorBlocks_ThrowsAndLeavesTheHandlerUninvoked() {
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(new BlockingCommandAdvisor());
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new RenameWidget("hub")));

        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_WithNoHandlerRegistered_Throws() {
        using var scope = BuildScope(_ => { });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new RenameWidget("hub")));

        Assert.Contains(typeof(RenameWidget).FullName!, error.Message);
    }

    [Fact]
    public async Task Dispatch_WithTwoHandlersRegistered_Throws() {
        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(new Mock<IRequestHandler<RenameWidget, string>>().Object);
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(new Mock<IRequestHandler<RenameWidget, string>>().Object);
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new RenameWidget("hub")));

        Assert.Contains(typeof(RenameWidget).FullName!, error.Message);
    }

    [Fact]
    public async Task CommandDispatcher_SendAsync_ForAResultlessCommand_InvokesTheHandlerWithoutNamingUnit() {
        var handler = new Mock<IRequestHandler<RetireWidget, Unit>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RetireWidget>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Unit.Value);

        using var scope = BuildScope(services =>
            services.AddSingleton<IRequestHandler<RetireWidget, Unit>>(handler.Object));

        await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>().SendAsync(new RetireWidget("hub"));

        handler.Verify(h => h.HandleAsync(It.IsAny<RetireWidget>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Dispatcher_ResolvesTheSameInstance_ThroughAllThreeContracts() {
        using var scope = BuildScope(_ => { });

        var concrete = scope.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>();
        var command  = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var query    = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();
        var request  = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();

        Assert.Same(concrete, command);
        Assert.Same(concrete, query);
        Assert.Same(concrete, request);
    }

    [Fact]
    public async Task Dispatch_EstablishesTheAmbientAdviceContext_VisibleToHandlerAndRestoredAfterward() {
        var advisor        = new TracingCommandAdvisor(0, "advisor", [], AdviseResult.Continue);
        AdviceContext? seenInHandler = null;
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()))
               .Returns(() => {
                    seenInHandler = AdviceContext.Current;
                    return Task.FromResult("hub");
                });

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(advisor);
        });

        Assert.Null(AdviceContext.Current);

        await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                   .SendAsync<RenameWidget, string>(new RenameWidget("hub"));

        Assert.NotNull(seenInHandler);
        Assert.Same(advisor.ObservedContext, seenInHandler);
        Assert.Null(AdviceContext.Current);
    }

    [Fact]
    public async Task Dispatch_RunsCommandAdvisorsInAscendingOrder_AndStopsAtTheFirstNonContinue() {
        var trail   = new List<string>();
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            // Registered out of order on purpose: the pipeline sorts by Order, not by registration.
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(
                new TracingCommandAdvisor(30, "third", trail, AdviseResult.Block));
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(
                new TracingCommandAdvisor(10, "first", trail, AdviseResult.Continue));
            services.AddSingleton<ICommandAdvisor<RenameWidget>>(
                new TracingCommandAdvisor(20, "second", trail, AdviseResult.Block));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new RenameWidget("hub")));

        Assert.Equal(["first", "second"], trail);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IServiceScope BuildScope(Action<IServiceCollection> configure) {
        var services = new ServiceCollection();
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        configure(services);
        return services.BuildServiceProvider().CreateScope();
    }
}
