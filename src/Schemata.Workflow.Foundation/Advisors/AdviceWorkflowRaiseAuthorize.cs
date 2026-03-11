using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

public sealed class AdviceWorkflowRaiseAuthorize : IWorkflowRaiseAdvisor
{
    private readonly IAccessProvider<SchemataWorkflow, WorkflowRequestContext<IEvent>> _access;

    public AdviceWorkflowRaiseAuthorize(IAccessProvider<SchemataWorkflow, WorkflowRequestContext<IEvent>> access) {
        _access = access;
    }

    #region IWorkflowRaiseAdvisor Members

    public int Order => 100_000_000;

    public int Priority => Order;

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
