using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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

        using var app      = builder.Build();
        var       registry = app.Services.GetRequiredService<IResourceRegistry>();

        Assert.NotNull(registry.GetResource(typeof(SchemataJob)));
        Assert.NotNull(registry.GetResource(typeof(SchemataJobExecution)));

        var job = registry.GetResource(typeof(SchemataJob))!;
        Assert.Null(job.Operations);
        Assert.Equal(
            [HttpResourceAttribute.Name, GrpcResourceAttribute.Name],
            job.Endpoints!.OrderBy(endpoint => endpoint, StringComparer.Ordinal));

        var execution = registry.GetResource(typeof(SchemataJobExecution))!;
        Assert.Equal([Operations.Get, Operations.List, Operations.Delete], execution.Operations!);
    }

    [Fact]
    public void MapHttp_And_MapGrpc_Register_Run_Cancel_Wait_Custom_Methods() {
        var builder = WebApplication.CreateBuilder();
        builder.UseSchemata(schema => schema.UseScheduling().MapHttp().MapGrpc());

        using var app      = builder.Build();
        var       registry = app.Services.GetRequiredService<IResourceRegistry>();

        var run = Assert.Single(registry.GetMethods(typeof(SchemataJob)));
        Assert.Equal(Verbs.Run, run.Verb);
        Assert.Equal(typeof(RunJobHandler), run.Handler);

        var methods = registry.GetMethods(typeof(SchemataJobExecution));
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
