using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     Lowers a single-source plan subtree into a backend-native query and streams the result. A
///     driver enforces its own source-level access and row-level entitlement (it knows the concrete
///     row type), so Insight Foundation passes the request and principal rather than a pre-typed
///     entitlement expression.
/// </summary>
public interface ISourceDriver
{
    /// <summary>The driver name used in <see cref="SourceConfig.DriverName" /> and keyed registration.</summary>
    string Name { get; }

    /// <summary>The operators this driver can push.</summary>
    DriverCapabilities Capabilities { get; }

    /// <summary>Lowers and executes the subtree, streaming the filtered, projected rows.</summary>
    /// <param name="subPlan">The single-source subtree to lower.</param>
    /// <param name="request">The originating request (for security context).</param>
    /// <param name="principal">The caller principal.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<ISourceResult> ExecuteAsync(
        SubPlan           subPlan,
        QueryInsightRequest request,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default);
}
