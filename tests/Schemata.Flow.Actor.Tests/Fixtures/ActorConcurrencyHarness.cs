using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine.Extensions;
using Schemata.Messaging.Skeleton.Advisors;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;

namespace Schemata.Flow.Actor.Tests.Fixtures;

/// <summary>
///     Builds a bare-<see cref="ServiceCollection" /> Flow host over a shared-cache in-memory SQLite
///     database, with or without the Flow.Actor bridge installed. No ASP.NET Core host is started;
///     <see cref="SchemataBuilder.Invoke" /> flushes the staged features directly onto the target
///     collection.
/// </summary>
/// <remarks>
///     Each resolved <see cref="TestDbContext" /> opens its <em>own</em> ADO.NET connection against
///     the shared-cache URI rather than reusing one shared <see cref="SqliteConnection" /> instance.
///     A single shared connection object serializes every command that crosses it, which would
///     quietly serialize the very writes the concurrency suite exists to race; per-context
///     connections let 100 scopes issue genuinely overlapping reads and writes against the same
///     backing database, exactly like separate pooled connections would in production. The anchor
///     <see cref="Connection" /> stays open only to keep the shared-cache database alive for the
///     harness's lifetime — nothing routes commands through it.
/// </remarks>
public sealed class ActorConcurrencyHarness : IAsyncDisposable
{
    public required SqliteConnection                Connection { get; init; }
    public required ServiceProvider                  Root       { get; init; }
    public required RecordingCompleteActivityAdvisor Advisor    { get; init; }

    /// <param name="withActor">Installs the Flow.Actor bridge when <see langword="true" />; otherwise the control-group, unwrapped path.</param>
    public static async Task<ActorConcurrencyHarness> BuildAsync(bool withActor) {
        var connectionString = $"Data Source=file:{Guid.NewGuid():n}?mode=memory&cache=shared";
        var connection       = new SqliteConnection(connectionString);
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connectionString)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataProcess, EfCoreRepository<TestDbContext, SchemataProcess>>();
        services.AddRepository<SchemataProcessToken, EfCoreRepository<TestDbContext, SchemataProcessToken>>();
        services.AddRepository<SchemataProcessTransition, EfCoreRepository<TestDbContext, SchemataProcessTransition>>();
        services.AddRepository<SchemataProcessSource, EfCoreRepository<TestDbContext, SchemataProcessSource>>();
        services.AddRepository<SchemataProcessCompensation, EfCoreRepository<TestDbContext, SchemataProcessCompensation>>();
        services.AddScoped<IUnitOfWork<TestDbContext>, EfCoreUnitOfWork<TestDbContext>>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowCatchHandler, PermissiveFlowCatchHandler>());


        var advisor = new RecordingCompleteActivityAdvisor();
        services.AddSingleton<IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>>(advisor);

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        var flow    = builder.UseFlow().Use<ConcurrentActivityProcess>().UseStateMachine();
        if (withActor) {
            builder.UseActor();
            flow.UseActor();
        }

        builder.Invoke(services);

        var root = services.BuildServiceProvider();

        await using (var scope = root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        return new() { Connection = connection, Root = root, Advisor = advisor };
    }

    #region IAsyncDisposable Members

    public async ValueTask DisposeAsync() {
        await Root.DisposeAsync();
        await Connection.DisposeAsync();
    }

    #endregion
}
