using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Grpc;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Resource.Grpc;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class ReportConflictTransportShould
{
    private static WebAppFactory ConflictHost() => new("ConflictingReport");

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grpc_Snapshot_Reads_Reject_Conflict_Before_Persistence(bool list) {
        using var factory = ConflictHost();
        using var channel = factory.CreateGrpcChannel();
        var clientFactory = ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>());
        var client = channel.CreateGrpcService<IResourceService<SchemataReportSnapshot, SchemataReportSnapshot, SchemataReportSnapshot, SchemataReportSnapshot>>(clientFactory);

        var error = await Assert.ThrowsAsync<RpcException>(async () => {
            if (list) await client.ListAsync(new() { Parent = "reports/daily" });
            else await client.GetAsync(new() { CanonicalName = "reports/daily/snapshots/current" });
        });

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public void Both_Report_Scheduling_Builders_Fail_Through_Actual_Host_Start() {
        using var factory = new WebAppFactory("ConflictingReportScheduling");

        Assert.Throws<FailedPreconditionException>(() => factory.CreateClient());
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_Report_Scheduling_Rejects_Actual_Host_Start(bool selectedScheduling) {
        var path = Path.Combine(Path.GetTempPath(), $"report-scheduling-conflict-{Guid.NewGuid():n}.db");
        try {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                Args = ["--urls", "http://127.0.0.1:0"],
            });
            builder.Services.AddSingleton<IStartupFilter, ScheduleRoutingStartup>();
            builder.UseSchemata(schema => {
                schema.Services.AddDbContextFactory<ScheduleDbContext>(options => options
                    .UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                schema.Services.AddRepository<ScheduleReport, EfCoreRepository<ScheduleDbContext, ScheduleReport>>();
                schema.Services.AddRepository<SchemataReportSnapshot, EfCoreRepository<ScheduleDbContext, SchemataReportSnapshot>>();
                schema.Services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<ScheduleDbContext, SchemataReportSnapshotChunk>>();
                schema.Services.AddRepository<SchemataJob, EfCoreRepository<ScheduleDbContext, SchemataJob>>();
                schema.Services.AddRepository<SchemataJobExecution, EfCoreRepository<ScheduleDbContext, SchemataJobExecution>>();
                schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
                schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
                schema.Services.AddSchemataSchedulingRepositoryStore();
                schema.UseScheduling();
                var selected = schema.UseReport<ScheduleReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
                var conflicting = schema.UseReport<ConflictingReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
                if (selectedScheduling) selected.UseScheduling();
                conflicting.UseScheduling();
            });
            await using var app = builder.Build();
            await using (var scope = app.Services.CreateAsyncScope()) {
                await scope.ServiceProvider.GetRequiredService<ScheduleDbContext>().Database.EnsureCreatedAsync();
            }

            try {
                var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => app.StartAsync());

                Assert.Equal("FAILED_PRECONDITION", error.Status);
                Assert.Equal(412, error.Code);
            } finally {
                await app.StopAsync();
            }
        } finally {
            File.Delete(path);
        }
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData("GET", "/v1/reports")]
    [InlineData("GET", "/v1/reports/daily")]
    [InlineData("POST", "/v1/reports")]
    [InlineData("PATCH", "/v1/reports/daily")]
    [InlineData("DELETE", "/v1/reports/daily")]
    [InlineData("GET", "/v1/reports/daily/snapshots/current:read")]
    [InlineData("GET", "/v1/reports/daily/snapshots")]
    [InlineData("GET", "/v1/reports/daily/snapshots/current")]
    [InlineData("POST", "/v1/reports:generate")]
    public async Task Http_Report_Entry_Rejects_Conflict_Before_Persistence(string method, string path) {
        using var factory = ConflictHost();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PATCH") request.Content = JsonContent.Create(new { name = "reports/daily", definition = "{}" });

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FAILED_PRECONDITION", error.GetProperty("error").GetProperty("status").GetString());
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData("List")]
    [InlineData("Get")]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public async Task Grpc_Report_Crud_Rejects_Conflict_Before_Persistence(string operation) {
        using var factory = ConflictHost();
        using var channel = factory.CreateGrpcChannel();
        var clientFactory = ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>());
        var client = channel.CreateGrpcService<IResourceService<SchemataReport, SchemataReport, SchemataReport, SchemataReport>>(clientFactory);

        var error = await Assert.ThrowsAsync<RpcException>(async () => {
            switch (operation) {
                case "List": await client.ListAsync(new()); break;
                case "Get": await client.GetAsync(new() { CanonicalName = "reports/daily" }); break;
                case "Create": await client.CreateAsync(new() { Name = "daily", Definition = "{}" }); break;
                case "Update": await client.UpdateAsync(new() { CanonicalName = "reports/daily", Definition = "{}" }); break;
                case "Delete": await client.DeleteAsync(new() { CanonicalName = "reports/daily" }); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Grpc_Custom_Methods_Reject_Conflict_Before_Persistence() {
        using var factory = ConflictHost();

        var generate = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataReport, GenerateReportRequest, Operation>(factory,
                "generate", new() { Name = "daily", Sync = true }));
        Assert.Equal(StatusCode.FailedPrecondition, generate.StatusCode);

        var read = await Assert.ThrowsAsync<RpcException>(() =>
            Call<SchemataReportSnapshot, ReadSnapshotRequest, ReadSnapshotGrpcResponse>(factory,
                "read", new() { CanonicalName = "reports/daily/snapshots/current" }));
        Assert.Equal(StatusCode.FailedPrecondition, read.StatusCode);
    }

    private static async Task<TResponse> Call<TEntity, TRequest, TResponse>(WebAppFactory factory, string verb, TRequest request)
        where TEntity : class
        where TRequest : class
        where TResponse : class {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(TRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(TResponse));
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var method = new Method<TRequest, TResponse>(
            MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(TEntity)),
            GrpcResourceNaming.CustomMethodName(descriptor, verb),
            GrpcMarshallers.Create<TRequest>(model),
            GrpcMarshallers.Create<TResponse>(model));
        using var channel = factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), request);
        return await call.ResponseAsync;
    }

}
