using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Tenancy;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantJobShould
{
    [Fact]
    public async Task Host_Poller_Resolves_Each_Durable_Job_From_Its_Tenant_Scope() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observed = new List<(Guid Snapshot, Guid? Ambient)>();
        var services = new ServiceCollection();
        services.AddDbContextFactory<Database>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
        services.AddRepository<SchemataJob, EfCoreRepository<Database, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<Database, SchemataJobExecution>>();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<ProbeJob>("probe");
        services.AddSingleton<IScheduledJobRegistry>(registry);
        services.AddSingleton(observed);
        services.AddSchemataScheduling();
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddScoped<ProbeJob>()));
        services.Replace(ServiceDescriptor.Singleton<IMessageExecutionScopeFactory, TenantMessageExecutionScopeFactory<SchemataTenant>>());
        await using var root = services.BuildServiceProvider();
        var a = new SchemataTenant { Name = "a" };
        var b = new SchemataTenant { Name = "b" };
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(a, default);
            await manager.CreateAsync(b, default);
            var executions = setup.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            await executions.AddAsync(new() { Name = "a", Tenant = a.Uid.ToString("D"), JobKey = "probe", State = ExecutionState.Pending, StartTime = DateTime.UtcNow.AddMinutes(-1) });
            await executions.AddAsync(new() { Name = "b", Tenant = b.Uid.ToString("D"), JobKey = "probe", State = ExecutionState.Pending, StartTime = DateTime.UtcNow.AddMinutes(-1) });
            await executions.CommitAsync();
        }
        await root.GetRequiredService<JobExecutionDispatcher>().DispatchPendingAsync(default);
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        Assert.Contains((a.Uid, (Guid?)a.Uid), observed);
        Assert.Contains((b.Uid, (Guid?)b.Uid), observed);
        Assert.Equal(2, observed.Count);
        await using (var verify = root.CreateAsyncScope()) {
            Assert.All(await verify.ServiceProvider.GetRequiredService<Database>().Set<SchemataJobExecution>().ToListAsync(), row => Assert.Equal(ExecutionState.Succeeded, row.State));
            var executions = verify.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            await executions.AddAsync(new() { Name = "malformed", Tenant = "invalid", JobKey = "probe", State = ExecutionState.Pending, StartTime = DateTime.UtcNow.AddMinutes(-1) });
            await executions.CommitAsync();
        }
        await Assert.ThrowsAsync<TenantResolveException>(() => root.GetRequiredService<JobExecutionDispatcher>().DispatchPendingAsync(default));
        await using var final = root.CreateAsyncScope();
        var failed = await final.ServiceProvider.GetRequiredService<Database>().Set<SchemataJobExecution>().SingleAsync(row => row.Name == "malformed");
        Assert.Equal(ExecutionState.Failed, failed.State);
        Assert.NotNull(failed.EndTime);
        Assert.Equal(2, observed.Count);
    }

    public sealed class ProbeJob(SchemataTenant tenant, List<(Guid Snapshot, Guid? Ambient)> observed) : IScheduledJob
    {
        public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
            await Task.Yield();
            observed.Add((tenant.Uid, TenantContext.Current.Uid));
        }
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataTenant>().Ignore(row => row.Hosts);
            model.Entity<SchemataTenantHost>();
            model.Entity<SchemataJob>();
            model.Entity<SchemataJobExecution>();
            base.OnModelCreating(model);
        }
    }
}
