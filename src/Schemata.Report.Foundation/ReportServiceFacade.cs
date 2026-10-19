using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Report.Skeleton.Models;

namespace Schemata.Report.Foundation;

/// <summary>
///     The framework-owned <see cref="IReportService" /> every host resolves. It rejects use while
///     conflicting Report entity triples are registered, then delegates to the explicitly selected
///     custom implementation (builder <c>UseService&lt;TService&gt;</c>) or the Foundation default.
///     Applications replacing the public interface directly own its complete contract.
/// </summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
/// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
public sealed class ReportServiceFacade<TReport, TSnapshot, TChunk>(
    ReportRegistration registration,
    IServiceProvider   services
) : IReportService
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    private readonly ReportRegistration _registration = registration;
    private readonly IServiceProvider   _services     = services;

    private IReportService Selected() {
        var selected = _services.GetKeyedService<IReportService>(ReportConstants.Services.Selected);
        if (selected is not null) {
            _registration.EnsureSingleTriple<TReport>();
            return selected;
        }

        return _services.GetRequiredKeyedService<IReportService>(ReportConstants.Services.Default);
    }

    #region IReportService Members

    public ValueTask<ReportResult> RunAsync(
        ReportRequest     request,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    ) {
        return Selected().RunAsync(request, principal, ct);
    }

    public ValueTask<Operation> GenerateAsync(
        ReportRequest     request,
        CancellationToken ct = default
    ) {
        return Selected().GenerateAsync(request, ct);
    }

    #endregion
}
