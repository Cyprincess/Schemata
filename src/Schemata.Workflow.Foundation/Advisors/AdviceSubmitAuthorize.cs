using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
///     Authorization advisor for the workflow Submit pipeline.
/// </summary>
/// <remarks>
///     Runs at <see cref="Order" /> 100,000,000 in the Submit pipeline. Throws
///     <see cref="AuthorizationException" /> when the current user lacks the required workflow role claim.
///     Auto-registered when <see cref="SchemataWorkflowBuilder.WithAuthorization" /> is called.
/// </remarks>
public sealed class AdviceSubmitAuthorize : ISubmitAdvisor
{
    public const int DefaultOrder = AdviceSubmitAnonymous.DefaultOrder + 10_000_000;

    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequest<IStateful>> _access;

    /// <summary>
    ///     Initializes a new instance with the specified access and permission resolver.
    /// </summary>
    /// <param name="access">The access provider that evaluates role-based claims.</param>
    public AdviceSubmitAuthorize(IAccessProvider<SchemataWorkflow, WorkflowRequest<IStateful>> access) {
        _access = access;
    }

    #region ISubmitAdvisor Members

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext              ctx,
        WorkflowRequest<IStateful> request,
        ClaimsPrincipal            principal,
        CancellationToken          ct = default
    ) {
        if (ctx.Has<AnonymousGranted>()) {
            return AdviseResult.Continue;
        }

        var context = new AccessContext<WorkflowRequest<IStateful>> {
            Operation = nameof(WorkflowController.Submit), Request = request,
        };

        var result = await _access.HasAccessAsync(null, context, principal, ct);
        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
