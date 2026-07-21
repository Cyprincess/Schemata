using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;

namespace Schemata.Report.Skeleton;

/// <summary>Provides the query definition for a program-backed report.</summary>
/// <remarks>
///     Program-backed definitions are consumed through keyed dependency injection. The report definition's
///     provider key selects the corresponding <see cref="IReportDefinitionProvider" /> registration.
/// </remarks>
public interface IReportDefinitionProvider
{
    /// <summary>Builds the report query definition.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The query definition.</returns>
    ValueTask<QueryInsightRequest> GetDefinitionAsync(CancellationToken ct = default);
}
