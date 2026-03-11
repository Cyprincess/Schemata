using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Advisors;

public sealed class AdviceWorkflowSubmitAuthorize : IWorkflowSubmitAdvisor
{
    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequestContext<WorkflowRequest<IStateful>>> _access;

    public AdviceWorkflowSubmitAuthorize(
        IAccessProvider<SchemataWorkflow, WorkflowRequestContext<WorkflowRequest<IStateful>>> access
    ) {
        _access = access;
    }

    #region IWorkflowSubmitAdvisor Members

    public int Order => 100_000_000;

    public int Priority => Order;

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
