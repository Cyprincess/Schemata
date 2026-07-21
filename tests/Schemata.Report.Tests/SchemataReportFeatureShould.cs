using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation;
using Xunit;

namespace Schemata.Report.Tests;

public class SchemataReportFeatureShould
{
    [Fact]
    public void UseReport_Registers_Default_Entities() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseReport());

        using var app       = builder.Build();
        var resources = app.Services.GetRequiredService<IOptions<SchemataResourceOptions>>().Value.Resources;

        Assert.Contains(typeof(SchemataReport).TypeHandle, resources.Keys);
        Assert.Contains(typeof(SchemataReportSnapshot).TypeHandle, resources.Keys);
        Assert.DoesNotContain(typeof(SchemataReportSnapshotChunk).TypeHandle, resources.Keys);
    }

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

    [Fact]
    public void Snapshot_Resource_Registration_Restricts_To_List_Get() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseReport());

        using var app       = builder.Build();
        var resources = app.Services.GetRequiredService<IOptions<SchemataResourceOptions>>().Value.Resources;
        var snapshot  = resources[typeof(SchemataReportSnapshot).TypeHandle];

        Assert.Equal([Operations.List, Operations.Get], snapshot.Operations);
    }

    [Fact]
    public async Task Missing_Report_Repositories_Throw_On_Host_Start() {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.UseSchemata(schema => schema.UseReport());

        await using var app = builder.Build();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());

        Assert.Contains("AddRepository", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ReportWithoutCanonicalName : SchemataReport;

    [CanonicalName("reports/{report}")]
    [DisplayName("Report")]
    private sealed class AlternateReport : SchemataReport;
}
