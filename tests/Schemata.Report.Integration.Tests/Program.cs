using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Report.Skeleton;
using Schemata.Report.Integration.Tests.Fixtures;

var options = new WebApplicationOptions { Args = args };
var builder = WebApplication.CreateBuilder(options);
var dbPath = Path.Combine(Path.GetTempPath(), $"report-integration-{Identifiers.NewUid():n}.db");
var useScheduling = !string.Equals(builder.Environment.EnvironmentName, "WithoutScheduling", StringComparison.Ordinal);
builder.UseSchemata(schema => {
    schema.UseDeveloperExceptionPage();
    schema.Services.AddDbContextFactory<TestDbContext>(options => {
        options.UseSqlite($"Data Source={dbPath}");
        options.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
    });
    schema.Services.AddRepository<SourceRecord, EfCoreRepository<TestDbContext, SourceRecord>>();
    schema.Services.AddRepository<SchemataReport, EfCoreRepository<TestDbContext, SchemataReport>>();
    schema.Services.AddRepository<SchemataReportSnapshot, EfCoreRepository<TestDbContext, SchemataReportSnapshot>>();
    schema.Services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<TestDbContext, SchemataReportSnapshotChunk>>();
    schema.Services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
    schema.Services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReport>, AdviceAddReportName>());
    schema.Services.Configure<SchemataSchedulingOptions>(options => options.OperationPollInterval = TimeSpan.FromMilliseconds(10));
    schema.Services.AddDistributedMemoryCache();
    schema.Services.AddDistributedCache();

    var mapper = schema.UseMapster();
    mapper.Map<SchemataReport, SchemataReport>();
    mapper.Map<SchemataReportSnapshot, SchemataReportSnapshot>();

    schema.UseInsight(insight => {
        insight.UseAip().UseCel().UseOrdering();
        insight.AddRepositorySource("source-records", "source-records");
        insight.AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });
    if (useScheduling) {
        schema.UseScheduling().MapHttp().MapGrpc();
    }
    schema.UseResource().MapHttp().MapGrpc();
    var reports = schema.UseReport();
    reports.Define("dsl-records", definition => definition
        .From("source-records", "record")
        .Select("value"));
    reports.Define("periodic-records", definition => definition
        .From("source-records", "record")
        .Select("value")
        .Periodic(interval: TimeSpan.FromDays(1)));
    reports.MapHttp().MapGrpc();
    if (useScheduling) {
        reports.UseScheduling();
    }
});

var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
    database.Database.EnsureCreated();
    database.SourceRecords.AddRange(
        new SourceRecord { Uid = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "one", Value = 1 },
        new SourceRecord { Uid = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "two", Value = 2 },
        new SourceRecord { Uid = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "three", Value = 3 });
    database.SaveChanges();
}
app.Run();

namespace Schemata.Report.Integration.Tests
{
    public partial class Program;
}
