using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advices;

public sealed class AdviceRaiseAuthorize : IWorkflowRaiseAdvice
{
    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequestContext<IEvent>> _access;

    public AdviceRaiseAuthorize(IAccessProvider<SchemataWorkflow, WorkflowRequestContext<IEvent>> access) {
        _access = access;
    }

    #region IWorkflowRaiseAdvice Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        IEvent            request,
        HttpContext       http,
        CancellationToken ct = default) {
        var result = await _access.HasAccessAsync(null,
                                                  new() {
                                                      Operation = request.Event,
                                                      Request   = request,
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
