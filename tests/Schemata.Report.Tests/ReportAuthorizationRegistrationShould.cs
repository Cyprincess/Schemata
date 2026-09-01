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
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Commands;
using Schemata.Security.Skeleton.Advisors;
using Schemata.Security.Skeleton;
using Xunit;

using Schemata.Report.Skeleton.Models;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Tests;

public sealed class ReportAuthorizationRegistrationShould
{
    [Fact]
    public void Activation_Registers_Only_Its_Security_Stage() {
        var services = new ServiceCollection();
        var builder  = new Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(new(), services);

        builder.WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(ReportResult));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.DoesNotContain(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(ReportResult)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(ReportResult)), advisors);
    }

    [Fact]
    public void Combined_Activation_Registers_Both_Security_Stages() {
        var services = new ServiceCollection();
        var builder  = new Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(new(), services);

        builder.WithAuthentication().WithAuthorization();

        var envelope = typeof(ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(ReportResult));
        var advisors = services.Where(descriptor => descriptor.ServiceType == service).Select(descriptor => descriptor.ImplementationType).ToArray();

        Assert.Contains(typeof(AuthenticationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(ReportResult)), advisors);
        Assert.Contains(typeof(AuthorizationPipelineAdvisor<,>).MakeGenericType(envelope, typeof(ReportResult)), advisors);
    }

    [Fact]
    public void Authorization_Resolves_Run_Closure_To_Report_Operation_And_Entity() {
        var services = new ServiceCollection();
        new Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(new(), services).WithAuthorization();
        var envelope = typeof(ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(ReportResult));

        Assert.Contains(services, descriptor => descriptor.ServiceType == service
                                             && descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == service
                                                   && descriptor.ImplementationType == typeof(AuthenticationPipelineAdvisor<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, (string Operation, Type? Entity)>>();

        var actual = resolve(new(ReportOperations.Run, "reports/daily", new(new() { Name = "daily" }, null), null));

        Assert.Equal(ReportOperations.Run, actual.Operation);
        Assert.Equal(typeof(SchemataReport), actual.Entity);
    }

    [Fact]
    public void Authorization_Resolves_Generate_Closure_To_Report_Operation_And_Entity() {
        var services = new ServiceCollection();
        new Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(new(), services).WithAuthorization();
        var envelope = typeof(ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>);
        var service  = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, typeof(Operation));

        Assert.Contains(services, descriptor => descriptor.ServiceType == service
                                             && descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>, Operation>));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == service
                                                   && descriptor.ImplementationType == typeof(AuthenticationPipelineAdvisor<ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>, Operation>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<ResourceMethodRequest<SchemataReport, GenerateReportRequest, Operation>, (string Operation, Type? Entity)>>();

        var actual = resolve(new(ReportOperations.Generate, "reports/daily", new() { Name = "daily" }, null));

        Assert.Equal(ReportOperations.Generate, actual.Operation);
        Assert.Equal(typeof(SchemataReport), actual.Entity);
    }

    [Fact]
    public void Authorization_Resolves_Read_Closure_To_Read_Operation_And_Snapshot_Entity() {
        var services = new ServiceCollection();
        new Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(new(), services).WithAuthorization();
        var service = typeof(IRequestPipelineAdvisor<,>).MakeGenericType(typeof(ReadSnapshotRequest), typeof(ReadSnapshotResponse));

        Assert.Contains(services, descriptor => descriptor.ServiceType == service
                                             && descriptor.ImplementationType == typeof(AuthorizationPipelineAdvisor<ReadSnapshotRequest, ReadSnapshotResponse>));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == service
                                                   && descriptor.ImplementationType == typeof(AuthenticationPipelineAdvisor<ReadSnapshotRequest, ReadSnapshotResponse>));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolve = scope.ServiceProvider.GetRequiredService<Func<ReadSnapshotRequest, (string Operation, Type? Entity)>>();

        var actual = resolve(new() { Name = "reports/daily/snapshots/latest" });

        Assert.Equal(ReportOperations.Read, actual.Operation);
        Assert.Equal(typeof(SchemataReportSnapshot), actual.Entity);
    }
    [Fact]
    public async Task Anonymous_Run_Bypasses_Authentication_And_Authorization() {
        var resolver = new Mock<IPermissionResolver>(MockBehavior.Strict);
        var matcher  = new Mock<IPermissionMatcher>(MockBehavior.Strict);
        var request  = new ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>(ReportOperations.Run, "reports/daily", new(new() { Name = "daily" }, null), null);
        var authentication = new AuthenticationPipelineAdvisor<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>(
            value => (value.Verb, typeof(AnonymousReport)));
        var authorization = new AuthorizationPipelineAdvisor<ResourceMethodRequest<SchemataReport, RunReportRequest, ReportResult>, ReportResult>(
            value => (value.Verb, typeof(AnonymousReport)), resolver.Object, matcher.Object);
        var calls = 0;

        var result = await authentication.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request,
            ct => authorization.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), request, _ => {
                calls++;
                return Task.FromResult(new ReportResult());
            }, ct), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        resolver.VerifyNoOtherCalls();
        matcher.VerifyNoOtherCalls();
    }

    [Anonymous(ReportOperations.Run)]
    private sealed class AnonymousReport : SchemataReport;
}
