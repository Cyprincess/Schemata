using Schemata.Core.Building;
using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Schemata.Abstractions.Resource;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Messaging.Skeleton;
using Schemata.Common;
using Schemata.Core;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Grpc.Features;
using Schemata.Resource.Grpc.Runtime;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using Schemata.Report.Foundation.Handlers;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Tests;

public class SchemataReportGrpcFeatureShould
{
    [Fact]
    public void MapGrpc_Registers_Report_Custom_Methods() {
        SchemataBuilder? schemata = null;
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => {
            schemata = schema;
            schema.UseReport().MapGrpc();
        });

        using var app      = builder.Build();
        var       registry = app.Services.GetRequiredService<ResourceRegistry>();

        Assert.True(schemata!.HasFeature<SchemataReportGrpcFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>());
        Assert.Equal(
            SchemataReportFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>.DefaultPriority + 200_000,
            new SchemataReportGrpcFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>().Priority);
        Assert.NotNull(registry.GetResource(typeof(SchemataReport)));
        Assert.NotNull(registry.GetResource(typeof(SchemataReportSnapshot)));

        var generate = Assert.Single(registry.GetMethods(typeof(SchemataReport)));
        Assert.Equal(Verbs.Generate, generate.Verb);
        Assert.Equal(typeof(GenerateHandler<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>), generate.Handler);
        Assert.Equal(ResourceMethodScope.Collection, generate.Scope);
        Assert.Equal(typeof(Operation), ResponseType(generate.Handler));
        Assert.Equal("GenerateReport", GrpcResourceNaming.CustomMethodName(ResourceNameDescriptor.ForType(typeof(SchemataReport)), generate.Verb));

        var read = Assert.Single(registry.GetMethods(typeof(SchemataReportSnapshot)));
        Assert.Equal(Verbs.Read, read.Verb);
        Assert.Equal(typeof(ReadSnapshotHandler<SchemataReportSnapshot>), read.Handler);
        Assert.Equal(ResourceMethodScope.Instance, read.Scope);
        Assert.Equal(typeof(ReadSnapshotResponse), ResponseType(read.Handler));
        Assert.Equal("ReadSnapshot", GrpcResourceNaming.CustomMethodName(ResourceNameDescriptor.ForType(typeof(SchemataReportSnapshot)), read.Verb));
    }

    [Fact]
    public void MapGrpc_Does_Not_Duplicate_Report_Custom_Methods_When_Repeated() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseReport().MapGrpc().MapGrpc());

        using var app      = builder.Build();
        var       registry = app.Services.GetRequiredService<ResourceRegistry>();

        Assert.Single(registry.GetMethods(typeof(SchemataReport)), method => method.Verb == Verbs.Generate);
        Assert.Single(registry.GetMethods(typeof(SchemataReportSnapshot)), method => method.Verb == Verbs.Read);
    }

    private static Type ResponseType(Type handler) {
        return handler.GetInterfaces()
                      .Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                      .GetGenericArguments()[1];
    }
}
