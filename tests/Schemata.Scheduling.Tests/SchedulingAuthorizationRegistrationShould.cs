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
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton.Advisors;
using Xunit;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;
using Operation = Schemata.Abstractions.Resource.Operation;

namespace Schemata.Scheduling.Tests;

public sealed class SchedulingAuthorizationRegistrationShould
{
    [Fact]
    public void Activation_Registers_Only_Its_Security_Stage() {
        var services = new ServiceCollection();
        var builder  = new SchedulingBuilder<SchemataJob, SchemataJobExecution>(new(), services);

        builder.WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(Operation));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.DoesNotContain(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(Operation)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(Operation)), advisors);
    }

    [Fact]
    public void Combined_Activation_Registers_Both_Security_Stages() {
        var services = new ServiceCollection();
        var builder  = new SchedulingBuilder<SchemataJob, SchemataJobExecution>(new(), services);

        builder.WithAuthentication().WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(Operation));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.Contains(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(Operation)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(Operation)), advisors);
    }

    [Fact]
    public void Authorization_Registers_Run_Envelope_And_Resolver() {
        var services = new ServiceCollection();
        new SchedulingBuilder<SchemataJob, SchemataJobExecution>(new(), services).WithAuthorization();
        var envelope = typeof(ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>);
        var response = typeof(Operation);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, response)
                                             && descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, response));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, ResourceTarget>>();

        var actual = resolve(new(Verbs.Run, "jobs/sample", new RunJobRequest { CanonicalName = "jobs/sample" }, null));

        Assert.Equal(Verbs.Run, actual.Operation);
        Assert.Equal(typeof(SchemataJob), actual.Entity);
    }
    [Fact]
    public async Task Anonymous_Run_Bypasses_Authentication_And_Authorization() {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var matcher  = new Mock<IPermissionMatcher>(MockBehavior.Strict);
        var request  = new ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>(Verbs.Run, "jobs/sample", new RunJobRequest { CanonicalName = "jobs/sample" }, null);
        var authentication = new AuthenticationPipelineAdvisor<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>(
            value => ResourceTarget.Instance(value.Verb, typeof(AnonymousJob), value.Name));
        var authorization = new AuthorizationPipelineAdvisor<ResourceMethodRequest<SchemataJob, RunJobRequest, Operation>, Operation>(
            value => ResourceTarget.Instance(value.Verb, typeof(AnonymousJob), value.Name), resolver.Object, matcher.Object, provider);
        var calls = 0;

        var result = await authentication.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request,
            ct => authorization.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, _ => {
                calls++;
                return Task.FromResult(new Operation());
            }, ct), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        resolver.VerifyNoOtherCalls();
        matcher.VerifyNoOtherCalls();
    }

    [Anonymous(Verbs.Run)]
    private sealed class AnonymousJob : SchemataJob;
}
