using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessRegistrySourceShould
{
    [Fact]
    public async Task Reject_Duplicate_Explicit_Source_Declarations() {
        var registry = Registry();

        await Assert.ThrowsAsync<InvalidArgumentException>(async () => await registry.RegisterAsync<DuplicateBindingProcess>());
    }

    [Fact]
    public async Task Reject_Same_Binding_Name_With_Different_Source_Types() {
        var registry = Registry();

        await Assert.ThrowsAsync<InvalidArgumentException>(async () => await registry.RegisterAsync<DifferentTypeBindingProcess>());
    }

    [Fact]
    public async Task Reject_Declarations_Targeting_Same_Member_Of_Same_Type() {
        var registry = Registry();

        await Assert.ThrowsAsync<InvalidArgumentException>(async () => await registry.RegisterAsync<ConflictingMemberProcess>());
    }

    [Fact]
    public async Task Replace_Default_Condition_Entry_With_Explicit_Declaration() {
        var registry = Registry();

        await registry.RegisterAsync<DefaultReplacementProcess>();

        var registration = registry.GetRegistration(nameof(DefaultReplacementProcess));
        var descriptor = Assert.IsType<FlowSourceDescriptor>(registration!.SourceTypes["order"]);
        Assert.Equal(typeof(Order), descriptor.SourceType);
        Assert.NotNull(descriptor.GetState);
        Assert.NotNull(descriptor.SetState);
    }

    [Fact]
    public async Task Reject_Member_Selector_That_Is_Not_Writable_String_Property() {
        var registry = Registry();

        await Assert.ThrowsAsync<InvalidArgumentException>(async () => await registry.RegisterAsync<ReadOnlyMemberProcess>());
    }

    [Fact]
    public async Task Warn_When_Stateless_Type_Has_No_State_Member() {
        var logger = new Mock<ILogger<ProcessRegistry>>();
        var registry = Registry(logger);

        await registry.RegisterAsync<StatelessSourceProcess>();

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static ProcessRegistry Registry(Mock<ILogger<ProcessRegistry>>? logger = null) {
        var runtime = new Mock<IFlowRuntime>();
        runtime.SetupGet(r => r.Capabilities).Returns(FlowRuntimeCapabilities.All);

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IEnumerable<IFlowEngineValidator>)))
                .Returns(Array.Empty<IFlowEngineValidator>());
        services.As<IKeyedServiceProvider>()
                .Setup(s => s.GetKeyedService(typeof(IFlowRuntime), It.IsAny<object?>()))
                .Returns(runtime.Object);

        if (logger is not null) {
            services.Setup(s => s.GetService(typeof(ILogger<ProcessRegistry>))).Returns(logger.Object);
        }

        return new(services.Object);
    }

    public sealed class DuplicateBindingProcess : ProcessDefinition
    {
        public DuplicateBindingProcess() {
            BindSource<Order>("order");
            BindSource<Order>("order");
        }
    }

    public sealed class DifferentTypeBindingProcess : ProcessDefinition
    {
        public DifferentTypeBindingProcess() {
            BindSource<Order>("source");
            BindSource<Invoice>("source");
        }
    }

    public sealed class ConflictingMemberProcess : ProcessDefinition
    {
        public ConflictingMemberProcess() {
            BindSource<Order>("first", order => order.Phase);
            BindSource<Order>("second", order => order.Phase);
        }
    }

    public sealed class DefaultReplacementProcess : ProcessDefinition
    {
        public DefaultReplacementProcess() {
            BindSource<Order>("order", order => order.Phase);
            this.Start().Go(Review);
            this.During(Review).Decide(
                this.When<Order>("order", _ => true).Go(Approved),
                this.Otherwise().Go(Rejected));
            this.During(Approved).End();
            this.During(Rejected).End();
        }

        public UserTask Review { get; } = null!;

        public UserTask Approved { get; } = null!;

        public UserTask Rejected { get; } = null!;
    }

    public sealed class ReadOnlyMemberProcess : ProcessDefinition
    {
        public ReadOnlyMemberProcess() { BindSource<ReadOnlyOrder>(order => order.Phase); }
    }

    public sealed class StatelessSourceProcess : ProcessDefinition
    {
        public StatelessSourceProcess() { BindSource<StatelessSource>(); }
    }

    public sealed class Order : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        public string? Phase { get; set; }
    }

    public sealed class Invoice : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    public sealed class ReadOnlyOrder : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }

        public string? Phase { get; }
    }

    public sealed class StatelessSource : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
