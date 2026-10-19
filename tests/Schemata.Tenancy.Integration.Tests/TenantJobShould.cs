using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantJobShould
{
    [Fact]
    public async Task Host_Dispatch_Reads_Host_Rows_And_Tenant_Containers_Dispatch_Their_Own() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var observed = new List<(Guid? TenantUid, Guid? Ambient)>();
        var services = new ServiceCollection();
        services.AddDbContextFactory<Database>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
        services.AddRepository<TenantJob, EfCoreRepository<Database, TenantJob>>();
        services.AddRepository<TenantJobExecution, EfCoreRepository<Database, TenantJobExecution>>();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<ProbeJob>("probe");
        services.AddSingleton<IScheduledJobRegistry>(registry);
        services.AddSingleton(observed);
        services.AddScoped<ProbeJob>();
        services.AddSchemataScheduling<TenantJob, TenantJobExecution>();
        services.AddSchemataSchedulingRepositoryStore<TenantJob, TenantJobExecution>();
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        new SchemataTenancyBuilder<SchemataTenant>(services).UseMessaging().UseRepositories(builder => {
            builder.Services.AddScoped<IJobExecutionStore<TenantJob, TenantJobExecution>, RepositoryJobExecutionStore<TenantJob, TenantJobExecution>>();
            builder.Services.AddScoped<JobExecutionDispatcher<TenantJob, TenantJobExecution>>();
            builder.Services.AddTransient<IRepository<TenantJob>, EfCoreRepository<Database, TenantJob>>();
            builder.Services.AddTransient<IRepository<TenantJobExecution>, EfCoreRepository<Database, TenantJobExecution>>();
            builder.Services.AddScoped<IResourceMutation<TenantJob>, ResourceMutation<TenantJob>>();
            builder.Services.AddScoped<IResourceMutation<TenantJobExecution>, ResourceMutation<TenantJobExecution>>();
        });
        await using var root = services.BuildServiceProvider();
        var tenants = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
        var a = new SchemataTenant { Name = "a" };
        var b = new SchemataTenant { Name = "b" };
        await using (var setup = root.CreateAsyncScope()) {
            await setup.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
            var manager = setup.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(a, default);
            await manager.CreateAsync(b, default);
        }
        await using (var host = root.CreateAsyncScope()) {
            var executions = host.ServiceProvider.GetRequiredService<IRepository<TenantJobExecution>>();
            await executions.AddAsync(new() {
                Name           = "host",
                JobKey         = "probe",
                State          = ExecutionState.Pending,
                StartTime      = DateTime.UtcNow.AddMinutes(-1),
                MessageContext = new() { [TenancyConstants.Messaging.TenantIdKey] = a.Uid.ToString("D") },
            });
            await executions.CommitAsync();
        }
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<TenantJobExecution>>();
            await executions.AddAsync(new() {
                Name = tenant.Name, JobKey = "probe", State = ExecutionState.Pending, StartTime = DateTime.UtcNow.AddMinutes(-1),
            });
            await executions.CommitAsync();
        }
        await root.GetRequiredService<JobExecutionDispatcher<TenantJob, TenantJobExecution>>().DispatchPendingAsync(default);
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        Assert.Equal([((Guid?)null, (Guid?)null)], observed);
        await using (var verify = root.CreateAsyncScope()) {
            var database = verify.ServiceProvider.GetRequiredService<Database>();
            var rows = await database.Set<TenantJobExecution>().ToListAsync();
            Assert.Equal(ExecutionState.Succeeded, rows.Single(row => row.Name == "host").State);
            Assert.All(rows.Where(row => row.Name != "host"), row => Assert.Equal(ExecutionState.Pending, row.State));
        }
        foreach (var tenant in new[] { a, b }) {
            await using var scope = await tenants.CreateAsync(new(tenant.Uid));
            using var entered = TenantContext.Enter(new(tenant.Uid));
            await scope.ServiceProvider.GetRequiredService<JobExecutionDispatcher<TenantJob, TenantJobExecution>>().DispatchPendingAsync(default);
        }
        Assert.Equal(TenantIdentity.Host, TenantContext.Current);
        Assert.Equal(
            new[] { ((Guid?)null, (Guid?)null), (a.Uid, (Guid?)a.Uid), (b.Uid, (Guid?)b.Uid) }.OrderBy(value => value.Item1),
            observed.OrderBy(value => value.Item1));
        await using (var verify = root.CreateAsyncScope()) {
            var database = verify.ServiceProvider.GetRequiredService<Database>();
            Assert.All(await database.Set<TenantJobExecution>().ToListAsync(), row => Assert.Equal(ExecutionState.Succeeded, row.State));
        }
    }

    public sealed class ProbeJob(List<(Guid? TenantUid, Guid? Ambient)> observed) : IScheduledJob
    {
        public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
            await Task.Yield();
            observed.Add((Assert.IsType<TenantJobExecution>(context.Execution).TenantUid, TenantContext.Current.Uid));
        }
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataTenant>().Ignore(row => row.Hosts);
            model.Entity<SchemataTenantHost>();
            model.Entity<TenantJob>();
            model.Entity<TenantJobExecution>();
            base.OnModelCreating(model);
        }
    }
}
