using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Order;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton.Entities;

var options = new WebApplicationOptions { Args = args };
var builder = WebApplication.CreateBuilder(options);
var dbPath = builder.Configuration["ReportDatabasePath"]
          ?? Path.Combine(Path.GetTempPath(), $"report-integration-{Guid.NewGuid():n}.db");
var useScheduling = !string.Equals(builder.Environment.EnvironmentName, "WithoutScheduling", StringComparison.Ordinal)
                 && !string.Equals(builder.Environment.EnvironmentName, "ConflictingReport", StringComparison.Ordinal);
var typedSequence = new List<decimal?> { null, 7922816251426433759354395033.5m };
var typedMap = new Dictionary<string, decimal?> { { "ExactKey", 7922816251426433759354395033.5m } };
var typedEmptyMap = new Dictionary<string, int>();
var precisionChildren = new List<PrecisionChild> { new() { Amount = 7922816251426433759354395033.5m, Number = 7 } };
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
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshot>, AdviceAddResourceName<SchemataReportSnapshot>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshotChunk>, AdviceAddResourceName<SchemataReportSnapshotChunk>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
    schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
    schema.Services.Configure<SchemataSchedulingOptions>(options => options.OperationPollInterval = TimeSpan.FromMilliseconds(10));
    schema.Services.AddMemoryCacheProvider();

    var mapper = schema.UseMapster();
    mapper.Map<SchemataReport, SchemataReport>();
    mapper.Map<SchemataReportSnapshot, SchemataReportSnapshot>();

    schema.UseInsight(insight => {
        insight.UseAip().UseCel().UseOrdering();
        insight.AddRepositorySource<SourceRecord, SourceQuery>("source-records", r => new SourceQuery { Value = r.Value, Secret = r.Value });
        insight.AddRepositorySource<SourceRecord, ValueQuery>("typed-values", row => new ValueQuery {
            Unsigned = ulong.MaxValue, Precise = 7922816251426433759354395033.5m,
            Id = row.Value, Blob = new byte[] { 0, 255 }, Timestamp = new DateTime(638999999999999999, DateTimeKind.Utc),
            Identifier = Guid.Parse("3f0bafc4-298d-4521-a302-8d646c81a515"), Duration = TimeSpan.FromTicks(-1234567890123),
            Character = 'λ', Flag = false, Number = 1.25,
            Children = precisionChildren,
            Missing = null, Sequence = typedSequence,
            Map = typedMap,
            EmptyList = Array.Empty<int>(), EmptyMap = typedEmptyMap,
            Offset = new DateTimeOffset(2026, 10, 2, 3, 4, 5, TimeSpan.FromHours(5.5)),
        });
        insight.AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
    });
    if (useScheduling) {
        schema.UseScheduling().MapHttp().MapGrpc();
    }
    schema.UseResource().MapHttp().MapGrpc();
    var reports = schema.UseReport();
    if (builder.Environment.EnvironmentName is "ConflictingReport" or "ConflictingReportScheduling") {
        var conflicting = schema.UseReport<ConflictingReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
        if (useScheduling) conflicting.UseScheduling();
    }
    reports.Define("dsl-records", definition => definition
        .From("source-records", "record")
        .Select("value"));
    reports.Define("typed-values", definition => definition.From("typed-values", "value"));
    reports.Define("computed-values", definition => definition.From("source-records", "record")
        .SelectExpression("1.25", "number", "cel")
        .SelectExpression("18446744073709551615u", "unsigned", "cel")
        .SelectExpression("b'\\000\\xff'", "bytes", "cel"));
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
