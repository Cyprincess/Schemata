using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

public sealed class AdviceWorkflowGetAuthorize : IWorkflowGetAdvisor
{
    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequestContext<long>> _access;

    public AdviceWorkflowGetAuthorize(IAccessProvider<SchemataWorkflow, WorkflowRequestContext<long>> access) {
        _access = access;
    }

    #region IWorkflowGetAdvisor Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        HttpContext       http,
        CancellationToken ct = default
    ) {
        var result = await _access.HasAccessAsync(null, new() {
            Operation = nameof(WorkflowController.Get), Request = workflow.Id, Workflow = workflow,
        }, http.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
