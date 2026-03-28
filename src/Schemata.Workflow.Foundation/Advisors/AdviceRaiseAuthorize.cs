using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
///     Authorization advisor for the workflow Raise pipeline.
/// </summary>
/// <remarks>
///     Runs at <see cref="Order" /> 100,000,000 in the Raise pipeline. Applies entitlement filtering
///     (even for anonymous users) and throws <see cref="AuthorizationException" /> when the current
///     user lacks the required permission or the workflow falls outside their entitlement scope.
///     Auto-registered when <see cref="SchemataWorkflowBuilder.WithAuthorization" /> is called.
/// </remarks>
public sealed class AdviceRaiseAuthorize : IRaiseAdvisor
{
    public const int DefaultOrder = AdviceRaiseAnonymous.DefaultOrder + 10_000_000;

    private readonly IAccessProvider<SchemataWorkflow, IEvent>      _access;
    private readonly IEntitlementProvider<SchemataWorkflow, IEvent> _entitlement;

    public AdviceRaiseAuthorize(
        IAccessProvider<SchemataWorkflow, IEvent>      access,
        IEntitlementProvider<SchemataWorkflow, IEvent> entitlement
    ) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IRaiseAdvisor Members

    /// <inheritdoc />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        SchemataWorkflow  workflow,
        IEvent            request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var context = new AccessContext<IEvent> { Operation = request.Event, Request = request };

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
