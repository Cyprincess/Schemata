using System;
using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Schemata.Abstractions.Entities;
using Schemata.Report.Skeleton;
using Xunit;

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
    public void Second_UseReport_With_Different_Types_Throws() {
        var builder = WebApplication.CreateBuilder();

        var exception = Assert.Throws<InvalidOperationException>(() => {
            builder.UseSchemata(schema => {
                schema.UseReport();
                schema.UseReport<AlternateReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
            });
        });

        Assert.Contains("only one UseReport per host", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ReportWithoutCanonicalName : SchemataReport;

    [CanonicalName("reports/{report}")]
    [DisplayName("Report")]
    private sealed class AlternateReport : SchemataReport;
}
