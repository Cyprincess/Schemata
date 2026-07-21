using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Resolves named report definitions and enumerates reports with periodic schedules.</summary>
/// <remarks>
///     The single public registration is a composite over ordered internal stores: the configuration/DSL store
///     precedes the database store. <see cref="ResolveAsync" /> returns the first match. <see cref="ListPeriodicAsync" />
///     concatenates both sources while de-duplicating by report name, preserving the first source's report.
/// </remarks>
public interface IReportDefinitionStore
{
    /// <summary>Resolves a named report and its executable query definition.</summary>
    /// <param name="name">The report name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The report and query, or <see langword="null" /> when no source defines the name.</returns>
    ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(
        string            name,
        CancellationToken ct = default);

    /// <summary>Streams periodic report definitions in composite precedence order.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Periodic reports de-duplicated by name.</returns>
    IAsyncEnumerable<SchemataReport> ListPeriodicAsync(CancellationToken ct = default);
}
