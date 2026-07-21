using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Scheduling.Tests;

public class SchedulingTransportShould
{
    [Fact]
    public void MapHttp_And_MapGrpc_Register_Job_And_Execution_Resources() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseScheduling().MapHttp().MapGrpc());

        using var app     = builder.Build();
        var       options = app.Services.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;

        Assert.Contains(typeof(SchemataJob).TypeHandle, options.Resources.Keys);
        Assert.Contains(typeof(SchemataJobExecution).TypeHandle, options.Resources.Keys);

        var job = options.Resources[typeof(SchemataJob).TypeHandle];
        Assert.Null(job.Operations);
        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            job.Endpoints!.OrderBy(endpoint => endpoint, StringComparer.Ordinal));

        var execution = options.Resources[typeof(SchemataJobExecution).TypeHandle];
        Assert.Equal([Operations.Get, Operations.List, Operations.Delete], execution.Operations!);
    }

    [Fact]
    public void MapHttp_And_MapGrpc_Register_Run_Cancel_Wait_Custom_Methods() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseScheduling().MapHttp().MapGrpc());

        using var app     = builder.Build();
        var       options = app.Services.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;

        var run = Assert.Single(options.Methods[typeof(SchemataJob).TypeHandle]);
        Assert.Equal(Verbs.Run, run.Verb);
        Assert.Equal(typeof(RunJobHandler), run.Handler);

        var methods = options.Methods[typeof(SchemataJobExecution).TypeHandle];
        var cancel  = Assert.Single(methods, method => method.Verb == Verbs.Cancel);
        Assert.Equal(typeof(CancelOperationHandler), cancel.Handler);
        var wait = Assert.Single(methods, method => method.Verb == Verbs.Wait);
        Assert.Equal(typeof(WaitOperationHandler), wait.Handler);
    }

    [Fact]
    public void MapHttp_And_MapGrpc_Resolve_Custom_Method_Handlers() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseScheduling().MapHttp().MapGrpc());

        using var app   = builder.Build();
        using var scope = app.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RunJobHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CancelOperationHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WaitOperationHandler>());
    }
}
