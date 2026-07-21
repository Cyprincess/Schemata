using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Grpc.Features;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Internal;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

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

        using var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;

        Assert.True(schemata!.HasFeature<SchemataReportGrpcFeature>());
        Assert.Equal(
            SchemataReportFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>.DefaultPriority + 200_000,
            new SchemataReportGrpcFeature().Priority);
        Assert.Contains(typeof(SchemataReport).TypeHandle, options.Resources.Keys);
        Assert.Contains(typeof(SchemataReportSnapshot).TypeHandle, options.Resources.Keys);

        var generate = Assert.Single(options.Methods[typeof(SchemataReport).TypeHandle]);
        Assert.Equal(Verbs.Generate, generate.Verb);
        Assert.Equal(typeof(GenerateHandler<SchemataReport>), generate.Handler);
        Assert.Equal(ResourceMethodScope.Collection, generate.Scope);
        Assert.Equal(typeof(Operation), ResponseType(generate.Handler));
        Assert.Equal("GenerateReport", GrpcResourceNaming.CustomMethodName(ResourceNameDescriptor.ForType(typeof(SchemataReport)), generate.Verb));

        var read = Assert.Single(options.Methods[typeof(SchemataReportSnapshot).TypeHandle]);
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

        using var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;

        Assert.Single(options.Methods[typeof(SchemataReport).TypeHandle], method => method.Verb == Verbs.Generate);
        Assert.Single(options.Methods[typeof(SchemataReportSnapshot).TypeHandle], method => method.Verb == Verbs.Read);
    }

    private static Type ResponseType(Type handler) {
        return handler.GetInterfaces()
                      .Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IResourceMethodHandler<,,>))
                      .GetGenericArguments()[2];
    }
}
