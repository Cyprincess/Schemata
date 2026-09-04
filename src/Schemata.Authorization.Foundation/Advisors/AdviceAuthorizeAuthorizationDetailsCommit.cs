using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeAuthorizationDetailsCommit{TApp}" />.</summary>
public static class AdviceAuthorizeAuthorizationDetailsCommit
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeAutoApproveSignIn.DefaultOrder + 1_000;
}

/// <summary>
///     Commits the accepted rich authorization grant set onto the authorization request model, per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-6">
///     RFC 9396: OAuth 2.0 Rich Authorization
///     Requests §6: Authorization Request Processing
/// </seealso>
///     . The request model then carries the normalized JSON the validating advisor published —
///     or <c>null</c>, dropping the raw parameter — when it reaches the interaction payload
///     serialization.
/// </summary>
/// <remarks>
///     Ordered behind <see cref="AdviceAuthorizeAutoApproveSignIn{TApp,TAuth}" /> so auto-approved
///     requests, which return <see cref="AdviseResult.Handle" /> and end the pipeline, keep the raw
///     request model.
/// </remarks>
public sealed class AdviceAuthorizeAuthorizationDetailsCommit<TApp> : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeAuthorizationDetailsCommit.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        // Rich authorization is feature-scoped: the validating advisor publishes the accepted
        // grant set on the context, and stamping here keeps the raw parameter from reaching the
        // interaction payload and the grant when no feature published one.
        authz.Request!.AuthorizationDetails = ctx.TryGet<AuthorizationDetailsGrant>(out var details) ? details?.Json : null;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
