using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Tests;

public class SchemataReportTransportShould
{
    [Fact]
    public void MapHttp_And_MapGrpc_Register_Report_And_Snapshot_Resources() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseReport().MapHttp().MapGrpc());

        using var app      = builder.Build();
        var       registry = app.Services.GetRequiredService<IResourceRegistry>();

        Assert.NotNull(registry.GetResource(typeof(SchemataReport)));
        Assert.NotNull(registry.GetResource(typeof(SchemataReportSnapshot)));
        Assert.Null(registry.GetResource(typeof(SchemataReportSnapshotChunk)));

        var report = registry.GetResource(typeof(SchemataReport))!;
        Assert.Null(report.Operations);
        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            report.Endpoints!.OrderBy(endpoint => endpoint, StringComparer.Ordinal));

        var snapshot = registry.GetResource(typeof(SchemataReportSnapshot))!;
        Assert.Equal([Operations.List, Operations.Get], snapshot.Operations!);
        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            snapshot.Endpoints!.OrderBy(endpoint => endpoint, StringComparer.Ordinal));
    }

    [Fact]
    public void MapHttp_And_MapGrpc_Register_Generate_And_Read_Custom_Methods() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseReport().MapHttp().MapGrpc());

        using var app      = builder.Build();
        var       registry = app.Services.GetRequiredService<IResourceRegistry>();

        var generate = Assert.Single(registry.GetMethods(typeof(SchemataReport)));
        Assert.Equal(Verbs.Generate, generate.Verb);
        Assert.Equal(typeof(GenerateHandler<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>), generate.Handler);

        var read = Assert.Single(registry.GetMethods(typeof(SchemataReportSnapshot)));
        Assert.Equal(Verbs.Read, read.Verb);
        Assert.Equal(typeof(ReadSnapshotHandler<SchemataReportSnapshot>), read.Handler);
    }

    [Fact]
    public void MapHttp_And_MapGrpc_Resolve_Custom_Method_Handlers() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            schema.UseInsight();
            schema.UseReport().MapHttp().MapGrpc();
        });

        using var app   = builder.Build();
        using var scope = app.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GenerateHandler<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ReadSnapshotHandler<SchemataReportSnapshot>>());
    }

    [Fact]
    public void UseReport_Without_Transports_Registers_No_Resources() {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IResourceRegistry>(new ResourceRegistry());
        builder.UseSchemata(schema => schema.UseReport());

        using var app      = builder.Build();
        var       registry = app.Services.GetRequiredService<IResourceRegistry>();

        Assert.Null(registry.GetResource(typeof(SchemataReport)));
        Assert.Null(registry.GetResource(typeof(SchemataReportSnapshot)));
    }

    [Fact]
    public void UseReport_Without_Repositories_Builds_And_Resolves_Report_Service() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            schema.UseInsight();
            schema.UseReport();
        });

        using var app   = builder.Build();
        using var scope = app.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReportService>());
    }
}
