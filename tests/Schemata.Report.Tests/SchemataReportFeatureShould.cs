using System;
using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Report.Foundation;
using Xunit;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Tests;

public class SchemataReportFeatureShould
{
    [Fact]
    public void Derived_Entity_Without_CanonicalName_Throws_At_Startup() {
        var builder = WebApplication.CreateBuilder();

        var exception = Assert.Throws<InvalidOperationException>(() => {
            builder.UseSchemata(schema => schema.UseReport<ReportWithoutCanonicalName, SchemataReportSnapshot, SchemataReportSnapshotChunk>());
        });

        Assert.Contains("CanonicalName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Second_UseReport_With_Different_Types_Builds_But_Disables_Report_Operations() {
        var services = new ServiceCollection();
        var schemata  = new SchemataOptions();

        services.AddSchemataReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(schemata);
        // A different triple adds no second set of closures and does not fail the host build.
        services.AddSchemataReport<AlternateReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(schemata);

        using var provider     = services.BuildServiceProvider();
        var       registration = provider.GetRequiredService<ReportRegistration>();

        Assert.Throws<FailedPreconditionException>(
            () => registration.EnsureSingleTriple<SchemataReport>());
    }


    private sealed class ReportWithoutCanonicalName : SchemataReport;

    [CanonicalName("reports/{report}")]
    [DisplayName("Report")]
    private sealed class AlternateReport : SchemataReport;
}
