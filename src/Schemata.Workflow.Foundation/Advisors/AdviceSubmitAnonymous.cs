using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Workflow.Foundation.Advisors;

public sealed class AdviceSubmitAnonymous : ISubmitAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region ISubmitAdvisor Members

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext              ctx,
        WorkflowRequest<IStateful> request,
        ClaimsPrincipal            principal,
        CancellationToken          ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<SchemataWorkflow>(nameof(WorkflowController.Submit))) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
