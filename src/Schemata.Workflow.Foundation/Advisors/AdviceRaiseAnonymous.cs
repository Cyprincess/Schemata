using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Workflow.Foundation.Advisors;

public sealed class AdviceRaiseAnonymous : IRaiseAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IRaiseAdvisor Members

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        IEvent            request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<SchemataWorkflow>(request.Event)) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
