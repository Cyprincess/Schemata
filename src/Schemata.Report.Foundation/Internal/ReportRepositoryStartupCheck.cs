using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schemata.Entity.Repository;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation.Internal;

internal sealed class ReportRepositoryStartupCheck<TReport, TSnapshot, TChunk> : IHostedService
    where TReport : SchemataReport
    where TSnapshot : SchemataReportSnapshot
    where TChunk : SchemataReportSnapshotChunk
{
    private readonly IServiceScopeFactory _scopes;

    public ReportRepositoryStartupCheck(IServiceScopeFactory scopes) {
        _scopes = scopes;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        using var scope = _scopes.CreateScope();
        RequireRepository<TReport>(scope.ServiceProvider);
        RequireRepository<TSnapshot>(scope.ServiceProvider);
        RequireRepository<TChunk>(scope.ServiceProvider);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }

    private static void RequireRepository<TEntity>(IServiceProvider services)
        where TEntity : class {
        if (services.GetService<IRepository<TEntity>>() is not null) {
            return;
        }

        throw new InvalidOperationException(
            $"Schemata Report requires IRepository<{typeof(TEntity).Name}>. "
            + $"Register it with AddRepository<{typeof(TEntity).Name}, TRepository>() before UseReport()."
        );
    }
}
