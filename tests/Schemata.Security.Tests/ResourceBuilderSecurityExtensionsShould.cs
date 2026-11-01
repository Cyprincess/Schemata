using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Insight.Foundation;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;
using SchemataFlowBuilder = Schemata.Flow.Foundation.Builders.SchemataFlowBuilder<
    Schemata.Flow.Skeleton.Entities.SchemataProcess,
    Schemata.Flow.Skeleton.Entities.SchemataProcessToken,
    Schemata.Flow.Skeleton.Entities.SchemataProcessTransition,
    Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation,
    Schemata.Flow.Skeleton.Entities.SchemataProcessParticipant,
    Schemata.Flow.Skeleton.Entities.SchemataProcessSource,
    Schemata.Flow.Skeleton.Entities.SchemataFlowEffectIntent>;

namespace Schemata.Security.Tests;

[Trait("Layer", "Component")]
public sealed class ResourceBuilderSecurityExtensionsShould
{
    [Fact]
    public void Preserve_The_Concrete_Builder_Through_Security_Activation() {
        Assert.IsType<SchemataResourceBuilder>(ActivateResource());
        Assert.IsType<SchemataFlowBuilder>(ActivateFlow());
        Assert.IsType<Report.Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(ActivateReport());
        Assert.IsType<SchedulingBuilder<SchemataJob, SchemataJobExecution>>(ActivateScheduling());
    }

    [Fact]
    public void Reject_A_Builder_Without_Security_Registration() {
        var exception = Assert.Throws<InvalidOperationException>(() => new UnregisteredBuilder().WithAuthentication());

        Assert.Contains("ResourceSecurityRegistration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_Insight_Authorization_Activation() {
        var exception = Assert.Throws<InvalidOperationException>(() => new SchemataInsightBuilder(new(), new ServiceCollection()).WithAuthorization());

        Assert.Contains("InsightSecurityGate", exception.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void Store_Insight_Authentication_Scheme() {
        var builder = new SchemataInsightBuilder(new(), new ServiceCollection()).WithAuthentication("test");

        Assert.Equal("test", builder.Schemata.Get<string>("Insight:AuthenticationScheme"));
    }
    private static SchemataResourceBuilder ActivateResource() {
        return new SchemataResourceBuilder(new(), new ServiceCollection()).WithAuthentication("test").WithAuthorization();
    }

    private static SchemataFlowBuilder ActivateFlow() {
        return new SchemataFlowBuilder(new(), new ServiceCollection()).WithAuthentication("test").WithAuthorization();
    }

    private static Report.Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk> ActivateReport() {
        return new Report.Foundation.SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(new(), new ServiceCollection()).WithAuthentication("test").WithAuthorization();
    }

    private static SchedulingBuilder<SchemataJob, SchemataJobExecution> ActivateScheduling() {
        return new SchedulingBuilder<SchemataJob, SchemataJobExecution>(new(), new ServiceCollection()).WithAuthentication("test").WithAuthorization();
    }


    private sealed class UnregisteredBuilder : IResourceBuilder
    {
        public SchemataOptions Schemata { get; } = new();

        public IServiceCollection Services { get; } = new ServiceCollection();
    }
}
