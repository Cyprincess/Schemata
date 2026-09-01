using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Security.Skeleton.Advisors;
using Xunit;
using Schemata.Security.Skeleton;

namespace Schemata.Flow.Tests;

public sealed class FlowAuthorizationRegistrationShould
{
    [Fact]
    public void Activation_Registers_Only_Its_Security_Stage() {
        var services = new ServiceCollection();
        var builder  = new SchemataFlowBuilder(new(), services);

        builder.WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service)
                               .Select(descriptor => descriptor.ImplementationType)
                               .ToArray();

        Assert.DoesNotContain(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess)), advisors);
    }

    [Fact]
    public void Combined_Activation_Registers_Both_Security_Stages() {
        var services = new ServiceCollection();
        var builder  = new SchemataFlowBuilder(new(), services);

        builder.WithAuthentication().WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service)
                               .Select(descriptor => descriptor.ImplementationType)
                               .ToArray();

        Assert.Contains(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess)), advisors);
    }

    [Fact]
    public void Authorization_Registers_And_Resolves_Each_Flow_Closure() {
        var services = new ServiceCollection();
        new SchemataFlowBuilder(new(), services).WithAuthorization();

        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, new(FlowOperations.Start, null, new("process", null, null, null, null, null), null), FlowOperations.Start, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, Foundation.Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, new(FlowOperations.Complete, "processes/p1", new("processes/p1", null, null), null), FlowOperations.Complete, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, Foundation.Commands.CorrelateMessageRequest, ProcessSnapshot>, ProcessSnapshot>(services, new(FlowOperations.Correlate, "processes/p1", new("processes/p1", "message", null, null, null), null), FlowOperations.Correlate, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, Foundation.Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, new(FlowOperations.Signal, null, new("signal", null, null, null), null), FlowOperations.Signal, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, DeliverSignalRequest, SignalDeliveryResult>, SignalDeliveryResult>(services, new(FlowOperations.Deliver, "processes/p1", new("processes/p1", "signal", null, null, null), null), FlowOperations.Deliver, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, TerminateProcessRequest, ProcessSnapshot>, ProcessSnapshot>(services, new(FlowOperations.Terminate, "processes/p1", new("processes/p1", null), null), FlowOperations.Terminate, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, new(FlowOperations.Cancel, "processes/p1/tokens/t1", new("processes/p1", "processes/p1/tokens/t1", null), null), FlowOperations.Cancel, typeof(SchemataProcessToken));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, RunEventRequest, ProcessSnapshot>, ProcessSnapshot>(services, new(FlowOperations.RunEvent, "processes/p1", new("processes/p1", null, new Mock<IEventDefinition>().Object, null), null), FlowOperations.RunEvent, typeof(SchemataProcess));
    }

    private static void VerifyAuthorization<TEnvelope, TResponse>(ServiceCollection services, TEnvelope envelope, string operation, Type entity)
        where TEnvelope : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class {
        var service = typeof(IRequestPipelineAdvisor<TEnvelope, TResponse>);
        var descriptors = services.Where(descriptor => descriptor.ServiceType == service).ToArray();

        var descriptor = Assert.Single(descriptors);
        Assert.Equal(typeof(AuthorizationPipelineAdvisor<TEnvelope, TResponse>), descriptor.ImplementationType);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<TEnvelope, (string Operation, Type? Entity)>>();

        var actual = resolve(envelope);

        Assert.Equal(operation, actual.Operation);
        Assert.Equal(entity, actual.Entity);
    }
    [Fact]
    public async Task Anonymous_Start_Bypasses_Authentication_And_Authorization() {
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var matcher  = new Mock<IPermissionMatcher>(MockBehavior.Strict);
        var request  = new ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>(FlowOperations.Start, null, new("process", null, null, null, null, null), null);
        var authentication = new AuthenticationPipelineAdvisor<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(
            value => (value.Verb, typeof(AnonymousProcess)));
        var authorization = new AuthorizationPipelineAdvisor<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(
            value => (value.Verb, typeof(AnonymousProcess)), resolver.Object, matcher.Object);
        var calls = 0;

        var result = await authentication.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request,
            ct => authorization.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, _ => {
                calls++;
                return Task.FromResult(new SchemataProcess());
            }, ct), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        resolver.VerifyNoOtherCalls();
        matcher.VerifyNoOtherCalls();
    }

    [Anonymous(FlowOperations.Start)]
    private sealed class AnonymousProcess : SchemataProcess;
}
