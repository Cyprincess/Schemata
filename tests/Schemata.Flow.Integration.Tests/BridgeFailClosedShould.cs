using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class BridgeFailClosedShould : IClassFixture<EfCoreFlowFixture>
{
    private readonly EfCoreFlowFixture _fixture;

    public BridgeFailClosedShould(EfCoreFlowFixture fixture) { _fixture = fixture; }

    public static IEnumerable<object[]> MixedGatewayCases => [
        [SchemataConstants.FlowEngines.StateMachine, Array.Empty<string>(), "message-catch", "UseEvent()"],
        [SchemataConstants.FlowEngines.Bpmn, Array.Empty<string>(), "message-catch", "UseEvent()"],
        [SchemataConstants.FlowEngines.StateMachine, new[] { SchemataFlowOptions.EventsBridge }, "timer-catch", "UseScheduling()"],
        [SchemataConstants.FlowEngines.Bpmn, new[] { SchemataFlowOptions.EventsBridge }, "timer-catch", "UseScheduling()"],
        [SchemataConstants.FlowEngines.StateMachine, new[] { SchemataFlowOptions.TimersBridge }, "message-catch", "UseEvent()"],
        [SchemataConstants.FlowEngines.Bpmn, new[] { SchemataFlowOptions.TimersBridge }, "message-catch", "UseEvent()"],
    ];

    [Fact]
    public void UseEvent_Declares_Events_Bridge() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseFlow().UseEvent());
        using var services = builder.Services.BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<SchemataFlowOptions>>().Value;
        Assert.Contains(SchemataFlowOptions.EventsBridge, options.Bridges);
    }

    [Fact]
    public void UseScheduling_Declares_Timers_Bridge() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseFlow().UseScheduling());
        using var services = builder.Services.BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<SchemataFlowOptions>>().Value;
        Assert.Contains(SchemataFlowOptions.TimersBridge, options.Bridges);
    }

    [Theory]
    [InlineData(typeof(BpmnDirectMessageBridgeProcess), SchemataFlowOptions.EventsBridge, "message-catch", "UseEvent()")]
    [InlineData(typeof(BpmnDirectTimerBridgeProcess), SchemataFlowOptions.TimersBridge, "timer-catch", "UseScheduling()")]
    public async Task Start_Direct_Bpmn_Catch_Without_Bridge_Throws(
        Type   definition,
        string bridge,
        string catchName,
        string activation
    ) {
        ConfigureBridges();
        var process = await RegisterAsync(definition, SchemataConstants.FlowEngines.Bpmn);

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () => await StartAsync(process));

        Assert.Contains(catchName, exception.Message);
        Assert.Contains(activation, exception.Message);
        Assert.DoesNotContain(bridge, _fixture.FlowOptions.Bridges);
    }

    [Theory]
    [MemberData(nameof(MixedGatewayCases))]
    public async Task Start_Mixed_Event_Gateway_With_A_Missing_Bridge_Throws(
        string   engine,
        string[] bridges,
        string   catchName,
        string   activation
    ) {
        ConfigureBridges(bridges);
        var process = await RegisterAsync(typeof(MixedGatewayBridgeProcess), engine);

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () => await StartAsync(process));

        Assert.Contains(catchName, exception.Message);
        Assert.Contains(activation, exception.Message);
    }

    [Theory]
    [InlineData(SchemataConstants.FlowEngines.StateMachine)]
    [InlineData(SchemataConstants.FlowEngines.Bpmn)]
    public async Task Start_Boundary_Timer_Without_Scheduling_Bridge_Throws(string engine) {
        ConfigureBridges();
        var process = await RegisterAsync(typeof(BoundaryTimerBridgeProcess), engine);

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () => await StartAsync(process));

        Assert.Contains("boundary-timer", exception.Message);
        Assert.Contains("UseScheduling()", exception.Message);
    }

    [Theory]
    [InlineData(SchemataConstants.FlowEngines.StateMachine)]
    [InlineData(SchemataConstants.FlowEngines.Bpmn)]
    public async Task Start_Event_Gateway_Without_Event_Bridge_Throws(string engine) {
        ConfigureBridges();
        var process = await RegisterAsync(typeof(MessageGatewayBridgeProcess), engine);

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () => await StartAsync(process));

        Assert.Contains("start-message", exception.Message);
        Assert.Contains("UseEvent()", exception.Message);
    }

    [Theory]
    [InlineData(SchemataConstants.FlowEngines.StateMachine)]
    [InlineData(SchemataConstants.FlowEngines.Bpmn)]
    public async Task Trigger_Repark_Without_Event_Bridge_Throws(string engine) {
        ConfigureBridges(SchemataFlowOptions.EventsBridge);
        var process = await RegisterAsync(typeof(ReparkAfterTriggerBridgeProcess), engine);
        var started = await StartAsync(process);

        ConfigureBridges();
        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(async () =>
            await CorrelateAsync(started, "first-message"));

        Assert.Contains("second-message", exception.Message);
        Assert.Contains("UseEvent()", exception.Message);

        var token = await ReadTokenAsync(started.Name!);
        Assert.Equal("first-gateway", token.WaitingAtName);
    }

    [Theory]
    [InlineData(typeof(BpmnDirectMessageBridgeProcess), SchemataConstants.FlowEngines.Bpmn, SchemataFlowOptions.EventsBridge, "message-catch")]
    [InlineData(typeof(BpmnDirectTimerBridgeProcess), SchemataConstants.FlowEngines.Bpmn, SchemataFlowOptions.TimersBridge, "timer-catch")]
    public async Task Start_Direct_Bpmn_Catch_With_Its_Bridge_Parks(
        Type   definition,
        string engine,
        string bridge,
        string waitingAt
    ) {
        ConfigureBridges(bridge);
        var process = await RegisterAsync(definition, engine);
        var started = await StartAsync(process);

        var token = await ReadTokenAsync(started.Name!);
        Assert.Equal(waitingAt, token.WaitingAtName);
    }

    [Theory]
    [InlineData(SchemataConstants.FlowEngines.StateMachine)]
    [InlineData(SchemataConstants.FlowEngines.Bpmn)]
    public async Task Start_Mixed_Event_Gateway_With_Both_Bridges_Parks(string engine) {
        ConfigureBridges(SchemataFlowOptions.EventsBridge, SchemataFlowOptions.TimersBridge);
        var process = await RegisterAsync(typeof(MixedGatewayBridgeProcess), engine);
        var started = await StartAsync(process);

        var token = await ReadTokenAsync(started.Name!);
        Assert.Equal("gateway", token.WaitingAtName);
    }

    [Theory]
    [InlineData(SchemataConstants.FlowEngines.StateMachine)]
    [InlineData(SchemataConstants.FlowEngines.Bpmn)]
    public async Task Start_Boundary_Timer_With_Scheduling_Bridge_Remains_Active(string engine) {
        ConfigureBridges(SchemataFlowOptions.TimersBridge);
        var process = await RegisterAsync(typeof(BoundaryTimerBridgeProcess), engine);
        var started = await StartAsync(process);

        var token = await ReadTokenAsync(started.Name!);
        Assert.Equal("Active", token.State);
        Assert.Equal("host", token.StateName);
        Assert.Null(token.WaitingAtName);
    }

    [Theory]
    [InlineData(SchemataConstants.FlowEngines.StateMachine)]
    [InlineData(SchemataConstants.FlowEngines.Bpmn)]
    public async Task Trigger_Repark_With_Event_Bridge_Persists(string engine) {
        ConfigureBridges(SchemataFlowOptions.EventsBridge);
        var process = await RegisterAsync(typeof(ReparkAfterTriggerBridgeProcess), engine);
        var started = await StartAsync(process);

        await CorrelateAsync(started, "first-message");

        var token = await ReadTokenAsync(started.Name!);
        Assert.True(token.WaitingAtName is "second-gateway" or "second-message");
    }

    private void ConfigureBridges(params string[] bridges) {
        _fixture.FlowOptions.Bridges.Clear();
        foreach (var bridge in bridges) {
            _fixture.FlowOptions.Bridges.Add(bridge);
        }
    }

    private async Task<string> RegisterAsync(Type definition, string engine) {
        using var scope = _fixture.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
        var name     = $"{definition.Name}-{Guid.NewGuid():n}";
        await registry.RegisterAsync(new ProcessConfiguration {
            Name           = name,
            Engine         = engine,
            DefinitionType = definition,
        });
        return name;
    }

    private async Task<SchemataProcess> StartAsync(string process) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(process, null, null, CancellationToken.None);
    }

    private async Task CorrelateAsync(SchemataProcess process, string message) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        await runner.CorrelateAsync(process, message, (string?)null, null, null, CancellationToken.None);
    }

    private async Task<SchemataProcessToken> ReadTokenAsync(string process) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var token = await repository.FirstOrDefaultAsync(query => query.Where(current => current.Process == process));
        Assert.NotNull(token);
        return token!;
    }
}

public sealed class BpmnDirectMessageBridgeProcess : ProcessDefinition
{
    public BpmnDirectMessageBridgeProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var message = new Message { Name = "direct-message" };
        var catchEvent = new FlowEvent {
            Name       = "message-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, catchEvent, end]);
        Messages.Add(message);
        Flows.Add(new() { Source = start, Target = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}

public sealed class BpmnDirectTimerBridgeProcess : ProcessDefinition
{
    public BpmnDirectTimerBridgeProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var catchEvent = new FlowEvent {
            Name       = "timer-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = BridgeDefinitionHelpers.Timer("direct-timer"),
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, catchEvent, end]);
        Flows.Add(new() { Source = start, Target = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}

public sealed class MixedGatewayBridgeProcess : ProcessDefinition
{
    public MixedGatewayBridgeProcess() {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Name = "gateway" };
        var message = new Message { Name = "mixed-message" };
        var messageCatch = new FlowEvent {
            Name       = "message-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var timerCatch = new FlowEvent {
            Name       = "timer-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = BridgeDefinitionHelpers.Timer("mixed-timer"),
        };
        var messageEnd = new FlowEvent { Name = "message-end", Position = EventPosition.End };
        var timerEnd   = new FlowEvent { Name = "timer-end", Position = EventPosition.End };

        Elements.AddRange([start, gateway, messageCatch, timerCatch, messageEnd, timerEnd]);
        Messages.Add(message);
        Flows.Add(new() { Source = start, Target = gateway });
        Flows.Add(new() { Source = gateway, Target = messageCatch });
        Flows.Add(new() { Source = gateway, Target = timerCatch });
        Flows.Add(new() { Source = messageCatch, Target = messageEnd });
        Flows.Add(new() { Source = timerCatch, Target = timerEnd });
    }
}

public sealed class BoundaryTimerBridgeProcess : ProcessDefinition
{
    public BoundaryTimerBridgeProcess() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new UserTask { Name = "host" };
        var boundary = new FlowEvent {
            Name       = "boundary-timer",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = BridgeDefinitionHelpers.Timer("boundary-timer"),
        };
        var completed = new FlowEvent { Name = "completed", Position = EventPosition.End };
        var timedOut  = new FlowEvent { Name = "timed-out", Position = EventPosition.End };

        Elements.AddRange([start, host, boundary, completed, timedOut]);
        Flows.Add(new() { Source = start, Target = host });
        Flows.Add(new() { Source = host, Target = completed });
        Flows.Add(new() { Source = boundary, Target = timedOut });
    }
}

public sealed class MessageGatewayBridgeProcess : ProcessDefinition
{
    public MessageGatewayBridgeProcess() {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Name = "start-gateway" };
        var message = new Message { Name = "start-message" };
        var catchEvent = new FlowEvent {
            Name       = "start-message",
            Position   = EventPosition.IntermediateCatch,
            Definition = message,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, gateway, catchEvent, end]);
        Messages.Add(message);
        Flows.Add(new() { Source = start, Target = gateway });
        Flows.Add(new() { Source = gateway, Target = catchEvent });
        Flows.Add(new() { Source = catchEvent, Target = end });
    }
}

public sealed class ReparkAfterTriggerBridgeProcess : ProcessDefinition
{
    public ReparkAfterTriggerBridgeProcess() {
        var start         = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var firstGateway  = new EventBasedGateway { Name = "first-gateway" };
        var secondGateway = new EventBasedGateway { Name = "second-gateway" };
        var first = new Message { Name = "first-message" };
        var second = new Message { Name = "second-message" };
        var firstCatch = new FlowEvent {
            Name       = "first-message-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = first,
        };
        var secondCatch = new FlowEvent {
            Name       = "second-message",
            Position   = EventPosition.IntermediateCatch,
            Definition = second,
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };

        Elements.AddRange([start, firstGateway, firstCatch, secondGateway, secondCatch, end]);
        Messages.Add(first);
        Messages.Add(second);
        Flows.Add(new() { Source = start, Target = firstGateway });
        Flows.Add(new() { Source = firstGateway, Target = firstCatch });
        Flows.Add(new() { Source = firstCatch, Target = secondGateway });
        Flows.Add(new() { Source = secondGateway, Target = secondCatch });
        Flows.Add(new() { Source = secondCatch, Target = end });
    }
}

file static class BridgeDefinitionHelpers
{
    internal static TimerDefinition Timer(string name) {
        return new() {
            Name           = name,
            TimerType      = TimerType.Duration,
            TimeExpression = "PT1M",
        };
    }
}
