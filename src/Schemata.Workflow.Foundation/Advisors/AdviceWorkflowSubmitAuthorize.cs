using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
/// Authorization advisor for the workflow Submit pipeline.
/// </summary>
/// <remarks>
/// Runs at <see cref="Order"/> 100,000,000 in the Submit pipeline. Throws
/// <see cref="AuthorizationException"/> when the current user lacks the required workflow role claim.
/// Auto-registered when <see cref="SchemataWorkflowBuilder.WithAuthorization"/> is called.
/// </remarks>
public sealed class AdviceWorkflowSubmitAuthorize : IWorkflowSubmitAdvisor
{
    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequestContext<WorkflowRequest<IStateful>>> _access;

    /// <summary>
    /// Initializes a new instance with the specified access provider.
    /// </summary>
    /// <param name="access">The access provider that evaluates role-based claims.</param>
    public AdviceWorkflowSubmitAuthorize(
        IAccessProvider<SchemataWorkflow, WorkflowRequestContext<WorkflowRequest<IStateful>>> access
    ) {
        _access = access;
    }

    #region IWorkflowSubmitAdvisor Members

    public const int DefaultOrder = SchemataConstants.Orders.Base;

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext              ctx,
        WorkflowRequest<IStateful> request,
        HttpContext                http,
        CancellationToken          ct = default
    ) {
        var context = new WorkflowRequestContext<WorkflowRequest<IStateful>> {
            Operation = nameof(WorkflowController.Submit), Request = request,
        };

        var result = await _access.HasAccessAsync(null, context, http.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
