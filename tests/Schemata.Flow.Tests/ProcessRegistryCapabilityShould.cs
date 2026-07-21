using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessRegistryCapabilityShould
{
    [Fact]
    public async Task Register_Parallel_Gateway_With_Single_Token_Engine_Reports_Missing_Capability() {
        var runtime = new Mock<IFlowRuntime>();
        runtime.SetupGet(candidate => candidate.EngineName).Returns("limited");
        runtime.SetupGet(candidate => candidate.Capabilities).Returns(FlowRuntimeCapabilities.ProcedureTasks);
        var services = new ServiceCollection()
                      .AddKeyedSingleton<IFlowRuntime>("limited", runtime.Object)
                      .BuildServiceProvider();
        var registry = new ProcessRegistry(services);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.RegisterAsync(new() {
            Name           = "parallel",
            Engine         = "limited",
            DefinitionType = typeof(ParallelDefinition),
        }).AsTask());

        Assert.Contains("ParallelGateway", exception.Message);
        Assert.Contains("limited", exception.Message);
        Assert.Contains("MultiToken", exception.Message);
    }

    [Fact]
    public async Task Register_Definition_Within_Engine_Capabilities_Succeeds() {
        var runtime = new Mock<IFlowRuntime>();
        runtime.SetupGet(candidate => candidate.EngineName).Returns("limited");
        runtime.SetupGet(candidate => candidate.Capabilities).Returns(FlowRuntimeCapabilities.ProcedureTasks);
        var services = new ServiceCollection()
                      .AddKeyedSingleton<IFlowRuntime>("limited", runtime.Object)
                      .BuildServiceProvider();
        var registry = new ProcessRegistry(services);

        await registry.RegisterAsync(new() {
            Name           = "linear",
            Engine         = "limited",
            DefinitionType = typeof(LinearDefinition),
        });

        Assert.NotNull(registry.GetRegistration("linear"));
    }

    [Theory]
    [InlineData(typeof(MessageCatchDefinition))]
    [InlineData(typeof(SignalCatchDefinition))]
    [InlineData(typeof(TimerCatchDefinition))]
    public async Task Register_CatchEvent_Succeeds(Type processDefinitionType) {
        var registry = new ProcessRegistry(Services());

        await registry.RegisterAsync(new() {
            Name           = "catch",
            Engine         = "limited",
            DefinitionType = processDefinitionType,
        });

        Assert.NotNull(registry.GetRegistration("catch"));
    }

    private static IServiceProvider Services() {
        var runtime = new Mock<IFlowRuntime>();
        runtime.SetupGet(candidate => candidate.EngineName).Returns("limited");
        runtime.SetupGet(candidate => candidate.Capabilities).Returns(FlowRuntimeCapabilities.All);

        return new ServiceCollection().AddKeyedSingleton<IFlowRuntime>("limited", runtime.Object)
                                      .BuildServiceProvider();
    }

    public sealed class ParallelDefinition : ProcessDefinition
    {
        public ParallelDefinition() {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var fork  = new ParallelGateway { Name = "fork" };
            var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.Add(start);
            Elements.Add(fork);
            Elements.Add(end);
            Flows.Add(new() { Source = start, Target = fork });
            Flows.Add(new() { Source = fork, Target = end });
        }
    }

    public sealed class LinearDefinition : ProcessDefinition
    {
        public LinearDefinition() {
            var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
            var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
            Elements.Add(start);
            Elements.Add(end);
            Flows.Add(new() { Source = start, Target = end });
        }
    }

    public sealed class MessageCatchDefinition : ProcessDefinition
    {
        public MessageCatchDefinition() {
            Elements.Add(new FlowEvent {
                Name       = "message",
                Position   = EventPosition.IntermediateCatch,
                Definition = new Message(),
            });
        }
    }

    public sealed class SignalCatchDefinition : ProcessDefinition
    {
        public SignalCatchDefinition() {
            Elements.Add(new FlowEvent {
                Name       = "signal",
                Position   = EventPosition.Boundary,
                Definition = new Signal(),
            });
        }
    }

    public sealed class TimerCatchDefinition : ProcessDefinition
    {
        public TimerCatchDefinition() {
            Elements.Add(new FlowEvent {
                Name       = "timer",
                Position   = EventPosition.IntermediateCatch,
                Definition = new TimerDefinition(),
            });
        }
    }
}
