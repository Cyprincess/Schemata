using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Security.Skeleton;

namespace Schemata.Workflow.Skeleton.Security;

/// <summary>
/// Role-based access provider for workflow operations.
/// </summary>
/// <typeparam name="T">The workflow entity type.</typeparam>
/// <typeparam name="TRequest">The request context type.</typeparam>
/// <remarks>
/// Checks for claims in the format <c>workflow-{operation}-{entity}</c> with wildcard support for
/// either the operation or entity segment.
/// </remarks>
public class WorkflowAccessProvider<T, TRequest> : IAccessProvider<T, WorkflowRequestContext<TRequest>>
{
    #region IAccessProvider<T,WorkflowRequestContext<TRequest>> Members

    /// <inheritdoc />
    public Task<bool> HasAccessAsync(
        T?                                workflow,
        WorkflowRequestContext<TRequest>? context,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        const string role = "workflow-{operation}-{entity}";

        var entity    = typeof(T).Name.Kebaberize();
        var operation = context?.Operation.Kebaberize();

        if (principal is null) {
            return Task.FromResult(false);
        }

        // TODO: check for participation of workflow
        // workflow-get-xxx will allow reading all specified workflows
        // workflow-submit-xxx will allow to submit new workflow
        // with no role specified, users will be allowed to read participated workflows
        // role of other operation will allow user to do specified operation on specified participated workflows

        if (principal.HasClaim(ClaimTypes.Role, role.Replace("{operation}", operation).Replace("{entity}", entity))) {
            return Task.FromResult(true);
        }

        if (principal.HasClaim(ClaimTypes.Role, role.Replace("{operation}", "*").Replace("{entity}", entity))) {
            return Task.FromResult(true);
        }

        if (principal.HasClaim(ClaimTypes.Role, role.Replace("{operation}", operation).Replace("{entity}", "*"))) {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    #endregion
}
