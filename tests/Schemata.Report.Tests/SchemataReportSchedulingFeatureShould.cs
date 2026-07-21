using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Schemata.Core.Features;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Scheduling;
using Schemata.Report.Scheduling.Advisors;
using Schemata.Report.Scheduling.Features;
using Schemata.Report.Skeleton;
using Xunit;

namespace Schemata.Report.Tests;

public sealed class SchemataReportSchedulingFeatureShould
{
    [Fact]
    public void ConfigureServices_Without_Report_Feature_Registers_Bridge_Services() {
        var services = new ServiceCollection();

        Configure(BridgeFeature(), services);

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService)
                                    && d.ImplementationType == typeof(ReportSchedulingInitializer));
        Assert.Contains(services, d => d.ImplementationType == typeof(AdviceReportScheduleSync<SchemataReport>));
    }

    [Fact]
    public void ConfigureServices_Order_Against_Report_Feature_Does_Not_Matter() {
        var bridgeFirst = new ServiceCollection();
        Configure(BridgeFeature(), bridgeFirst);
        Configure(ReportFeature(), bridgeFirst);

        var reportFirst = new ServiceCollection();
        Configure(ReportFeature(), reportFirst);
        Configure(BridgeFeature(), reportFirst);

        foreach (var services in new[] { bridgeFirst, reportFirst }) {
            Assert.Contains(services, d => d.ImplementationType == typeof(ReportSchedulingInitializer));
            Assert.Contains(services, d => d.ServiceType == typeof(IReportService));
        }

        Assert.Equal(reportFirst.Count, bridgeFirst.Count);
    }

    private static FeatureBase BridgeFeature() {
        return new SchemataReportSchedulingFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
    }

    private static FeatureBase ReportFeature() {
        return new SchemataReportFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
    }

    private static void Configure(FeatureBase feature, IServiceCollection services) {
        feature.ConfigureServices(
            services,
            new(),
            new(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IWebHostEnvironment>()
        );
    }
}
