using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
/// Authorization advisor for the workflow Raise pipeline.
/// </summary>
/// <remarks>
/// Runs at <see cref="Order"/> 100,000,000 in the Raise pipeline. Throws
/// <see cref="AuthorizationException"/> when the current user lacks the required workflow role claim.
/// Auto-registered when <see cref="SchemataWorkflowBuilder.WithAuthorization"/> is called.
/// </remarks>
public sealed class AdviceWorkflowRaiseAuthorize : IWorkflowRaiseAdvisor
{
    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequestContext<IEvent>> _access;

    /// <summary>
    /// Initializes a new instance with the specified access provider.
    /// </summary>
    /// <param name="access">The access provider that evaluates role-based claims.</param>
    public AdviceWorkflowRaiseAuthorize(IAccessProvider<SchemataWorkflow, WorkflowRequestContext<IEvent>> access) {
        _access = access;
    }

    #region IWorkflowRaiseAdvisor Members

    public const int DefaultOrder = SchemataConstants.Orders.Base;

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        IEvent            request,
        HttpContext       http,
        CancellationToken ct = default
    ) {
        var result = await _access.HasAccessAsync(null, new() {
            Operation = request.Event, Request = request, Workflow = workflow,
        }, http.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
