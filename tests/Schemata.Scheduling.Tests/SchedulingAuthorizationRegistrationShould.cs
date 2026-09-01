using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton.Advisors;
using Xunit;
using Schemata.Security.Skeleton;

namespace Schemata.Scheduling.Tests;

public sealed class SchedulingAuthorizationRegistrationShould
{
    [Fact]
    public void Activation_Registers_Only_Its_Security_Stage() {
        var services = new ServiceCollection();
        var builder  = new SchedulingBuilder(new(), services);

        builder.WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataJobExecution));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.DoesNotContain(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataJobExecution)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataJobExecution)), advisors);
    }

    [Fact]
    public void Combined_Activation_Registers_Both_Security_Stages() {
        var services = new ServiceCollection();
        var builder  = new SchedulingBuilder(new(), services);

        builder.WithAuthentication().WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataJobExecution));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.Contains(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataJobExecution)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(SchemataJobExecution)), advisors);
    }

    [Fact]
    public void Authorization_Registers_Trigger_Envelope_And_Resolver() {
        var services = new ServiceCollection();
        new SchedulingBuilder(new(), services).WithAuthorization();
        var envelope = typeof(ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>);
        var response = typeof(SchemataJobExecution);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, response)
                                             && descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, response));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, (string Operation, Type? Entity)>>();

        var actual = resolve(new(SchedulingOperations.Trigger, "jobs/sample", new("jobs/sample", typeof(object), new()), null));

        Assert.Equal(SchedulingOperations.Trigger, actual.Operation);
        Assert.Equal(typeof(SchemataJob), actual.Entity);
    }
    [Fact]
    public async Task Anonymous_Trigger_Bypasses_Authentication_And_Authorization() {
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var matcher  = new Mock<IPermissionMatcher>(MockBehavior.Strict);
        var request  = new ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>(SchedulingOperations.Trigger, "jobs/sample", new("jobs/sample", typeof(object), new()), null);
        var authentication = new AuthenticationPipelineAdvisor<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(
            value => (value.Verb, typeof(AnonymousJob)));
        var authorization = new AuthorizationPipelineAdvisor<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(
            value => (value.Verb, typeof(AnonymousJob)), resolver.Object, matcher.Object);
        var calls = 0;

        var result = await authentication.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request,
            ct => authorization.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, _ => {
                calls++;
                return Task.FromResult(new SchemataJobExecution());
            }, ct), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        resolver.VerifyNoOtherCalls();
        matcher.VerifyNoOtherCalls();
    }

    [Anonymous(SchedulingOperations.Trigger)]
    private sealed class AnonymousJob : SchemataJob;
}
