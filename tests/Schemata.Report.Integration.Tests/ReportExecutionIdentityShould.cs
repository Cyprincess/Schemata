using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Advisors;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Models;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Layer", "Integration")]
public class ReportExecutionIdentityShould
{
    [Theory]
    [InlineData("users/one", true)]
    [InlineData("users/one", false)]
    [InlineData(null, true)]
    public async Task Replay_Durable_Context_Revalidates_Identity_Before_Publishing(string? subject, bool active) {
        var path = Path.Combine(Path.GetTempPath(), $"report-execution-{Guid.NewGuid():n}.db");
        var uid = Guid.NewGuid();
        try {
            await using (var provider = CreateProvider(path)) {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.SourceRecords.AddRange(new SourceRecord { Uid = Guid.NewGuid(), Name = "users/one", Value = 1 },
                    new SourceRecord { Uid = Guid.NewGuid(), Name = "two", Value = 2 });
                scope.ServiceProvider.GetRequiredService<ExecutionIdentity>().Subject = subject;
                var captured = MessageContexts.Capture(scope.ServiceProvider);
                db.JobExecutions.Add(new SchemataJobExecution {
                    Uid = uid, Name = "restart", CanonicalName = "operations/restart",
                    JobKey = "schemata.report.generate", State = ExecutionState.Running,
                    ArgsJson = JsonSerializer.Serialize(new ReportRequest {
                        Query = new QueryInsightRequest { Sources = [new("record", "source-records")] }, Persist = true,
                    }, SchemataJson.Default),
                    MessageContext = new(captured.Items),
                });
                await db.SaveChangesAsync();
            }

            await using var restarted = CreateProvider(path);
            await using var read = restarted.CreateAsyncScope();
            var database = read.ServiceProvider.GetRequiredService<TestDbContext>();
            var execution = await database.JobExecutions.AsNoTracking().SingleAsync(row => row.Uid == uid);
            if (!active) {
                var identity = await database.SourceRecords.SingleAsync(row => row.Name == "users/one");
                database.SourceRecords.Remove(identity);
                await database.SaveChangesAsync();
            }
            var job = ActivatorUtilities.CreateInstance<ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(restarted);
            var context = new JobContext { ExecutionUid = uid, Execution = execution, ArgsJson = execution.ArgsJson };
            if (subject is null || !active) {
                await Assert.ThrowsAsync<PermissionDeniedException>(() => job.ExecuteAsync(context, CancellationToken.None));
                Assert.Null(execution.Output);
                Assert.Empty(await database.ReportSnapshots.AsNoTracking().ToListAsync());
                Assert.Empty(await database.ReportSnapshotChunks.AsNoTracking().ToListAsync());
            } else {
                await job.ExecuteAsync(context, CancellationToken.None);
                var output = JsonSerializer.Deserialize<ReportOperationOutput>(execution.Output!, SchemataJson.Default)!;
                Assert.NotNull(output.Snapshot);
                var rows = new List<int>();
                await foreach (var row in read.ServiceProvider.GetRequiredService<IReportSnapshotStore>().ReadRowsAsync(output.Snapshot!)) {
                    rows.Add(Assert.IsType<JsonElement>(row["value"]).GetInt32());
                }
                Assert.Equal(new[] { 1 }, rows);
            }
        } finally {
            File.Delete(path);
        }
    }

    private static ServiceProvider CreateProvider(string path) {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite($"Data Source={path};Pooling=False")
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SourceRecord, EfCoreRepository<TestDbContext, SourceRecord>>();
        services.AddRepository<SchemataReport, EfCoreRepository<TestDbContext, SchemataReport>>();
        services.AddRepository<SchemataReportSnapshot, EfCoreRepository<TestDbContext, SchemataReportSnapshot>>();
        services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<TestDbContext, SchemataReportSnapshotChunk>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshot>, AdviceAddResourceName<SchemataReportSnapshot>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshotChunk>, AdviceAddResourceName<SchemataReportSnapshotChunk>>());
        services.AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>();
        services.AddScoped<ExecutionIdentity>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageContextPropagator, IdentityPropagator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReportGenerateAdvisor, ResolveIdentity>());
        services.AddScoped<IEntitlementProvider<SourceRecord, QueryInsightRequest>, IdentityEntitlement>();
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseInsight(insight => {
            insight.AddRepositorySource<SourceRecord, SourceQuery>("source-records", row => new SourceQuery { Value = row.Value, Secret = row.Value });
            insight.AddSourceDriver<RepositoryDriver>(RepositoryDriver.DriverName);
        });
        builder.UseReport();
        builder.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class ExecutionIdentity { public string? Subject { get; set; } }

    private sealed class IdentityPropagator : IMessageContextPropagator
    {
        public void Capture(IDictionary<string, string?> items, IServiceProvider source) =>
            items["application:report-subject"] = source.GetRequiredService<ExecutionIdentity>().Subject;

        public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default) {
            items.TryGetValue("application:report-subject", out var subject);
            target.GetRequiredService<ExecutionIdentity>().Subject = subject;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ResolveIdentity(ExecutionIdentity identity, TestDbContext database) : IReportGenerateAdvisor
    {
        public int Order => 0;
        public async Task<AdviseResult> AdviseAsync(AdviceContext context, ReportGenerateContext generation, CancellationToken ct) {
            Assert.Null(generation.Principal);
            var user = identity.Subject is null ? null
                : await database.SourceRecords.AsNoTracking().SingleOrDefaultAsync(row => row.Name == identity.Subject, ct);
            if (user is null) throw new PermissionDeniedException("Execution identity is absent or revoked.");
            generation.Principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Name!)], "application"));
            return AdviseResult.Continue;
        }
    }

    private sealed class IdentityEntitlement : IEntitlementProvider<SourceRecord, QueryInsightRequest>
    {
        public Task<Expression<Func<SourceRecord, bool>>?> GenerateEntitlementExpressionAsync(
            AccessContext<QueryInsightRequest> context, ClaimsPrincipal? principal, CancellationToken ct = default) {
            var allowed = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value == "users/one";
            Expression<Func<SourceRecord, bool>> filter = row => allowed && row.Value == 1;
            return Task.FromResult<Expression<Func<SourceRecord, bool>>?>(filter);
        }
    }
}
