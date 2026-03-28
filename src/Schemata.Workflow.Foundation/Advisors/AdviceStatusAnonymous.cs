using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Workflow.Foundation.Advisors;

public sealed class AdviceStatusAnonymous : IStatusAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IStatusAdvisor Members

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<SchemataWorkflow>(nameof(WorkflowController.Status))) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
