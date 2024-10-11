using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advices;

public sealed class AdviceGetAuthorize : IWorkflowGetAdvice
{
    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequestContext<long>> _access;

    public AdviceGetAuthorize(IAccessProvider<SchemataWorkflow, WorkflowRequestContext<long>> access) {
        _access = access;
    }

    #region IWorkflowGetAdvice Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        HttpContext       http,
        CancellationToken ct = default) {
        var result = await _access.HasAccessAsync(null,
                                                  new() {
                                                      Operation = nameof(WorkflowController.Get),
                                                      Request   = workflow.Id,
                                                      Workflow  = workflow,
                                                  },
                                                  http.User,
                                                  ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return true;
    }

    #endregion
}
