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
    public void Authorization_Activation_Registers_The_Selected_Security_Stages() {
        var envelope       = typeof(ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>);
        var service        = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess));
        var authentication = typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess));
        var authorization  = typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataProcess));

        var onlyAuthorization = new ServiceCollection();
        new SchemataFlowBuilder(new(), onlyAuthorization).WithAuthorization();

        var advisors = onlyAuthorization.Where(descriptor => descriptor.ServiceType == service)
                                        .Select(descriptor => descriptor.ImplementationType)
                                        .ToArray();
        Assert.DoesNotContain(authentication, advisors);
        Assert.Contains(authorization, advisors);

        var combined = new ServiceCollection();
        new SchemataFlowBuilder(new(), combined).WithAuthentication().WithAuthorization();

        advisors = combined.Where(descriptor => descriptor.ServiceType == service)
                           .Select(descriptor => descriptor.ImplementationType)
                           .ToArray();
        Assert.Contains(authentication, advisors);
        Assert.Contains(authorization, advisors);
    }

    [Fact]
    public void Authorization_Registers_And_Resolves_Each_Flow_Closure() {
        var services = new ServiceCollection();
        new SchemataFlowBuilder(new(), services).WithAuthorization();

        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, StartProcessRequest, SchemataProcess>, SchemataProcess>(services, new(FlowOperations.Start, null, new("process", null, null, null, null, null), null), FlowOperations.Start, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, Foundation.Commands.CompleteActivityRequest, ProcessSnapshot>, ProcessSnapshot>(services, new(FlowOperations.Complete, "processes/p1", new("processes/p1", null, null), null), FlowOperations.Complete, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcess, Foundation.Commands.ThrowSignalRequest, IReadOnlyList<SignalDeliveryResult>>, IReadOnlyList<SignalDeliveryResult>>(services, new(FlowOperations.Signal, null, new("signal", null, null, null), null), FlowOperations.Signal, typeof(SchemataProcess));
        VerifyAuthorization<ResourceMethodRequest<SchemataProcessToken, CancelTokenRequest, ProcessSnapshot>, ProcessSnapshot>(services, new(FlowOperations.Cancel, "processes/p1/tokens/t1", new("processes/p1", "processes/p1/tokens/t1", null), null), FlowOperations.Cancel, typeof(SchemataProcessToken));
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
