using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Foundation.Controllers;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
///     Authorization advisor for the workflow Get pipeline.
/// </summary>
/// <remarks>
///     Runs at <see cref="Order" /> 100,000,000 in the Get pipeline. Applies entitlement filtering
///     (even for anonymous users) and throws <see cref="AuthorizationException" /> when the current
///     user lacks the required permission or the workflow falls outside their entitlement scope.
///     Auto-registered when <see cref="SchemataWorkflowBuilder.WithAuthorization" /> is called.
/// </remarks>
public sealed class AdviceStatusAuthorize : IStatusAdvisor
{
    public const int DefaultOrder = AdviceStatusAnonymous.DefaultOrder + 10_000_000;

    private readonly IAccessProvider<SchemataWorkflow, Guid>      _access;
    private readonly IEntitlementProvider<SchemataWorkflow, Guid> _entitlement;

    public AdviceStatusAuthorize(
        IAccessProvider<SchemataWorkflow, Guid>      access,
        IEntitlementProvider<SchemataWorkflow, Guid> entitlement
    ) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IStatusAdvisor Members

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var context = new AccessContext<Guid> { Operation = nameof(WorkflowController.Status), Request = workflow.Uid };

        var expression = await _entitlement.GenerateEntitlementExpressionAsync(context, principal, ct);
        if (expression is not null && !expression.Compile()(workflow)) {
            throw new AuthorizationException();
        }

        if (ctx.Has<AnonymousGranted>()) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(workflow, context, principal, ct);
        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
