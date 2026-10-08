using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Messaging.Skeleton.Tests.Fixtures;
using Xunit;

namespace Schemata.Messaging.Skeleton.Tests;

public class RequestDispatcherShould
{
    [Fact]
    public async Task Dispatch_ForACommand_RunsThePipelineChainAroundTheHandler() {
        var trail   = new List<string>();
        var advisor = new OrderedRenameAdvisor(0, "advisor", trail, callNext: true);
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("hub");

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(advisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                                 .SendAsync<RenameWidget, string>(new("hub"));

        Assert.Equal("hub", result);
        Assert.Equal(["advisor:before", "advisor:after"], trail);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_ForAQuery_RunsThePipelineChain() {
        var trail   = new List<string>();
        var advisor = new QueryTracingPipelineAdvisor("query-advisor", trail);
        var handler = new Mock<IRequestHandler<CountWidgets, int>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<CountWidgets>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(7);

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<CountWidgets, int>>(handler.Object);
            services.AddSingleton<IRequestPipelineAdvisor<CountWidgets, int>>(advisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<IQueryDispatcher>()
                                 .SendAsync<CountWidgets, int>(new());

        Assert.Equal(7, result);
        Assert.Equal(["query-advisor"], trail);
        handler.Verify(h => h.HandleAsync(It.IsAny<CountWidgets>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatch_ForAPlainRequest_RunsNoPipelineChain() {
        var advisor = new RecordingPlainPipelineAdvisor();
        var handler = new Mock<IRequestHandler<PlainRequest, string>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<PlainRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("echo");

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<PlainRequest, string>>(handler.Object);
            services.AddSingleton<IRequestPipelineAdvisor<PlainRequest, string>>(advisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                                 .SendAsync<PlainRequest, string>(new("echo"));

        Assert.Equal("echo", result);
        Assert.False(advisor.Ran);
    }

    [Fact]
    public async Task Dispatch_WhenAnAdvisorShortCircuits_ReturnsItsResponseWithoutInvokingTheHandler() {
        var trail   = new List<string>();
        var advisor = new OrderedRenameAdvisor(0, "short", trail, callNext: false);
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(advisor);
        });

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                                 .SendAsync<RenameWidget, string>(new("hub"));

        Assert.Equal("short:short", result);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_WhenAnAdvisorShortCircuitsWithNoHandlerRegistered_DoesNotThrowMissingHandler() {
        var advisor = new OrderedRenameAdvisor(0, "short", [], callNext: false);

        using var scope = BuildScope(services =>
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(advisor));

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                                 .SendAsync<RenameWidget, string>(new("hub"));

        Assert.Equal("short:short", result);
    }

    [Fact]
    public async Task Dispatch_WhenAnAdvisorThrows_SurfacesTheAdvisorsOwnException() {
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(
                new ThrowingRenameAdvisor(new NotSupportedException("advisor-defined")));
        });

        var error = await Assert.ThrowsAsync<NotSupportedException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new("hub")));

        Assert.Equal("advisor-defined", error.Message);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Dispatch_WithNoHandlerRegistered_Throws() {
        using var scope = BuildScope(_ => { });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new("hub")));

        Assert.Equal($"No request handler registered for request type '{typeof(RenameWidget).FullName}'.", error.Message);
    }

    [Fact]
    public async Task Dispatch_WithTwoHandlersRegistered_Throws() {
        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(new Mock<IRequestHandler<RenameWidget, string>>().Object);
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(new Mock<IRequestHandler<RenameWidget, string>>().Object);
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                       .SendAsync<RenameWidget, string>(new("hub")));

        Assert.Equal(
            $"Multiple request handlers registered for request type '{typeof(RenameWidget).FullName}'. Expected exactly one.",
            error.Message);
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
    public async Task Dispatch_EstablishesTheAmbientAdviceContext_SharedWithAdvisorAndHandlerAndRestoredAfterward() {
        var advisor = new OrderedRenameAdvisor(0, "advisor", [], callNext: true);
        AdviceContext? seenInHandler = null;
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();
        handler.Setup(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()))
               .Returns(() => {
                    seenInHandler = AdviceContext.Current;
                    return Task.FromResult("hub");
                });

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(advisor);
        });

        Assert.Null(AdviceContext.Current);

        await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                   .SendAsync<RenameWidget, string>(new("hub"));

        Assert.NotNull(seenInHandler);
        Assert.Same(advisor.ObservedContext, seenInHandler);
        Assert.Null(AdviceContext.Current);
    }

    [Fact]
    public async Task Dispatch_RunsPipelineAdvisorsInAscendingOrder_AndAnEarlyShortCircuitStopsTheChain() {
        var trail   = new List<string>();
        var handler = new Mock<IRequestHandler<RenameWidget, string>>();

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(handler.Object);
            // Registered out of order on purpose: the pipeline sorts by Order, not by registration.
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(
                new OrderedRenameAdvisor(30, "third", trail, callNext: true));
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(
                new OrderedRenameAdvisor(10, "first", trail, callNext: true));
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(
                new OrderedRenameAdvisor(20, "second", trail, callNext: false));
        });

        var result = await scope.ServiceProvider.GetRequiredService<ICommandDispatcher>()
                                 .SendAsync<RenameWidget, string>(new("hub"));

        Assert.Equal("second:short", result);
        Assert.Equal(["first:before", "second:before", "first:after"], trail);
        handler.Verify(h => h.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Dispatch_WithAHandlerKey_RunsOrderedWrapsAndValidationAroundTheInnerHandler() {
        var trail = new List<string>();
        var widgets = new Dictionary<string, int> { ["old"] = 42 };
        var first = new OrderedRenameAdvisor(10, "first", trail, callNext: true);
        var validation = new OrderedRenameAdvisor(20, "validation", trail, callNext: true);
        var last = new OrderedRenameAdvisor(30, "last", trail, callNext: true);
        var wrapper = new Mock<IRequestHandler<RenameWidget, string>>(MockBehavior.Strict);
        wrapper.Setup(handler => handler.HandleAsync(It.IsAny<RenameWidget>(), It.IsAny<CancellationToken>()))
               .Throws(new InvalidOperationException("actor self-ask"));

        using var scope = BuildScope(services => {
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(wrapper.Object);
            services.AddKeyedScoped<IRequestHandler<RenameWidget, string>>("inner",
                (_, _) => new RenamingHandler(widgets, trail));
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(last);
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(first);
            services.AddKeyedSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(
                RequestPipelineStages.Validation, validation);
        });

        var selfAsk = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>()
                 .SendAsync<RenameWidget, string>(new("new"), ct: default));
        Assert.Equal("actor self-ask", selfAsk.Message);
        trail.Clear();

        var result = await scope.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>()
                                .SendAsync<RenameWidget, string>(new("new"), serviceKey: "inner");

        Assert.Equal("new", result);
        Assert.False(widgets.ContainsKey("old"));
        Assert.Equal(42, widgets["new"]);
        Assert.Equal([
            "first:before", "validation:before", "last:before", "handler",
            "last:after", "validation:after", "first:after",
        ], trail);
        Assert.Same(first.ObservedContext, validation.ObservedContext);
        Assert.Same(first.ObservedContext, last.ObservedContext);
        Assert.Null(AdviceContext.Current);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Dispatch_WithAHandlerKey_WhenShortCircuited_DoesNotConstructTheKeyedHandler() {
        using var scope = BuildScope(services => {
            services.AddKeyedScoped<IRequestHandler<RenameWidget, string>>("inner",
                (_, _) => throw new InvalidOperationException("handler constructed"));
            services.AddSingleton<IRequestPipelineAdvisor<RenameWidget, string>>(
                new OrderedRenameAdvisor(0, "cached", [], callNext: false));
        });

        var result = await scope.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>()
                                .SendAsync<RenameWidget, string>(new("new"), "inner");

        Assert.Equal("cached:short", result);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Dispatch_WithAMissingHandlerKey_PropagatesTheRequiredKeyedServiceError() {
        using var scope = BuildScope(services =>
            services.AddSingleton<IRequestHandler<RenameWidget, string>>(
                new Mock<IRequestHandler<RenameWidget, string>>(MockBehavior.Strict).Object));
        var expected = Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredKeyedService<IRequestHandler<RenameWidget, string>>("missing"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>()
                 .SendAsync<RenameWidget, string>(new("new"), "missing"));

        Assert.Equal(expected.Message, error.Message);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Dispatch_WithAPlainRequestAndHandlerKey_ValidatesBeforeHandlerResolution() {
        using var scope = BuildScope(services => {
            services.AddKeyedScoped<IRequestHandler<PlainRequest, string>>("inner",
                (_, _) => throw new InvalidOperationException("handler constructed"));
            services.AddScoped<IRequestPipelineAdvisor<PlainRequest, string>>(
                _ => throw new InvalidOperationException("ordinary wrap constructed"));
            services.AddKeyedSingleton<IRequestPipelineAdvisor<PlainRequest, string>>(
                RequestPipelineStages.Validation, new PlainValueValidationAdvisor());
        });

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.ServiceProvider.GetRequiredService<InProcessRequestDispatcher>()
                 .SendAsync<PlainRequest, string>(new(" "), "inner"));

        Assert.Equal("Value", error.ParamName);
    }

    private sealed class RenamingHandler(Dictionary<string, int> widgets, List<string> trail)
        : IRequestHandler<RenameWidget, string>
    {
        public Task<string> HandleAsync(RenameWidget request, CancellationToken ct = default) {
            widgets.Add(request.Name, widgets["old"]);
            widgets.Remove("old");
            trail.Add("handler");
            return Task.FromResult(request.Name);
        }
    }

    private sealed class PlainValueValidationAdvisor : IRequestPipelineAdvisor<PlainRequest, string>
    {
        public int Order => 0;

        public Task<string> AdviseAsync(
            AdviceContext ctx,
            PlainRequest request,
            RequestHandlerContinuation<string> next,
            CancellationToken ct = default) {
            if (string.IsNullOrWhiteSpace(request.Value)) {
                throw new ArgumentException("A value is required.", nameof(request.Value));
            }

            return next(ct);
        }
    }

    private static IServiceScope BuildScope(Action<IServiceCollection> configure) {
        var services = new ServiceCollection();
        services.AddInProcessRequestDispatcher();
        configure(services);
        return services.BuildServiceProvider().CreateScope();
    }
}
