using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Grpc.Services;
using Schemata.Flow.Http.Controllers;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Xunit;

namespace Schemata.Flow.Tests;

/// <summary>
///     Proves the raw <see cref="IQueryDispatcher" /> entry, the HTTP
///     <see cref="ProcessDefinitionsController" />, and the gRPC <see cref="ProcessDefinitionService" />
///     run the exact same <see cref="ListProcessDefinitionsQuery" /> pipeline: equivalent
///     <see cref="ProcessDefinitionInfo" /> projections and the registered
///     <see cref="IRequestPipelineAdvisor{TRequest,TResponse}" /> firing once per entry. The process registry is the only
///     mocked dependency.
/// </summary>
public sealed class ProcessDefinitionQueryEntryEquivalenceShould
{
    [Fact]
    public async Task List_Through_Dispatcher_Controller_And_Grpc_Produce_Equivalent_Infos_And_Fire_The_Same_Advisor() {
        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegisteredProcesses()).Returns(["orders"]);
        registry.Setup(r => r.GetRegistration("orders")).Returns(CreateRegistration());

        var advisor = new RecordingQueryAdvisor();

        await using var services = new ServiceCollection()
                                  .AddSingleton(registry.Object)
                                  .AddSingleton<IRequestPipelineAdvisor<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>>(advisor)
                                  .AddSchemataFlow()
                                  .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IQueryDispatcher>();

        var direct = await dispatcher.SendAsync<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>(
            new(), CancellationToken.None);
        Assert.Equal(1, advisor.Count);

        var controller = new ProcessDefinitionsController(dispatcher, Options.Create(new JsonSerializerOptions())) {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        var action = await controller.ListProcessDefinitions();
        var json   = Assert.IsType<JsonResult>(action);
        var http   = Assert.IsType<ListResultBase<ProcessDefinitionInfo>>(json.Value).Entities;
        Assert.Equal(2, advisor.Count);

        var service = new ProcessDefinitionService(dispatcher);
        var grpc    = (await service.ListProcessDefinitionsAsync(new())).Entities;
        Assert.Equal(3, advisor.Count);

        var info = Assert.Single(direct);
        Assert.Equal("definitions/orders", info.CanonicalName);
        Assert.Equal(8, info.Elements.Count);
        Assert.Equal(4, info.Flows.Count);
        Assert.Single(info.Messages);

        Assert.NotNull(http);
        Assert.NotNull(grpc);
        AssertEquivalent(info, Assert.Single(http));
        AssertEquivalent(info, Assert.Single(grpc));

    }

    private static void AssertEquivalent(ProcessDefinitionInfo expected, ProcessDefinitionInfo actual) {
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static ProcessRegistration CreateRegistration() {
        var message = new Message {
            Name         = "approve",
            DisplayName  = "Approve",
            Description  = "Approval message",
            DisplayNames = new() { ["zh-Hans"] = "批准消息" },
            Descriptions = new() { ["zh-Hans"] = "批准消息说明" },
        };
        var start = new StartEvent {
            Name         = "begin",
            DisplayName  = "Begin",
            Description  = "Start processing",
            DisplayNames = new() { ["zh-Hans"] = "开始" },
            Descriptions = new() { ["zh-Hans"] = "开始处理" },
        };
        var task = new NoneTask {
            Name                = "review",
            DisplayName         = "Review",
            Description         = "Review the order",
            LoopCharacteristics = new StandardLoopCharacteristics(),
        };
        var gateway = new EventBasedGateway { Name = "Await_review", DisplayName = "Await review" };
        var catchEvent = new FlowEvent {
            Name         = "Catch_Await_review_approve",
            DisplayName  = "Await approval",
            DisplayNames = new() { ["zh-Hans"] = "等待批准" },
            Position     = EventPosition.IntermediateCatch,
            Definition   = message,
        };
        var boundary = new FlowEvent {
            Name         = "On_review_approve",
            DisplayName  = "Review approved",
            Position     = EventPosition.Boundary,
            Definition   = message,
            AttachedTo   = task,
            Interrupting = false,
        };
        var subTask = new NoneTask { Name = "archive", DisplayName = "Archive" };
        var sub = new EmbeddedSubProcess {
            Name             = "eventSub",
            DisplayName      = "Event subprocess",
            TriggeredByEvent = true,
        };
        sub.Children.Add(subTask);
        var end = new EndEvent { Name = "done", DisplayName = "Done", IsTerminate = true };

        var definition = new ProcessDefinition {
            Name         = "orders",
            DisplayName  = "Orders",
            Description  = "Order fulfilment flow",
            DisplayNames = new() { ["zh-Hans"] = "订单流程" },
            Descriptions = new() { ["zh-Hans"] = "订单履约流程" },
        };
        definition.Elements.AddRange([start, task, gateway, catchEvent, boundary, sub, end]);
        definition.Flows.AddRange([
            new() {
                Source       = start,
                Target       = task,
                Condition    = Mock.Of<IConditionExpression>(),
                DisplayName  = "Begin review",
                Description  = "Move into review",
                DisplayNames = new() { ["zh-Hans"] = "开始复核" },
                Descriptions = new() { ["zh-Hans"] = "进入复核阶段" },
            },
            new() { Source = task, Target = gateway, IsDefault = true },
            new() { Source = gateway, Target = catchEvent },
            new() { Source = catchEvent, Target = end },
        ]);
        definition.Messages.Add(message);

        return new() {
            Name          = "orders",
            Engine        = "StateMachine",
            Definition    = definition,
            Configuration = new() { Name = "orders" },
        };
    }

    /// <summary>Records every dispatch of <see cref="ListProcessDefinitionsQuery" /> it observes.</summary>
    private sealed class RecordingQueryAdvisor : IRequestPipelineAdvisor<ListProcessDefinitionsQuery, IReadOnlyList<ProcessDefinitionInfo>>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<IReadOnlyList<ProcessDefinitionInfo>> AdviseAsync(
            AdviceContext                                                    ctx,
            ListProcessDefinitionsQuery                                      a1,
            RequestHandlerContinuation<IReadOnlyList<ProcessDefinitionInfo>> next,
            CancellationToken                                                ct = default) {
            Count++;
            return next(ct);
        }
    }
}
