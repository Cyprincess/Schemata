using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeResource" />.</summary>
public static class AdviceAuthorizeResource
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeScopeValidation.DefaultOrder + 10_000_000;

    /// <summary>
    ///     RFC 8707 §2 resource syntax: an absolute URI (scheme and host present) that carries no
    ///     fragment component and does not use the <c>urn</c> scheme. A query component is allowed;
    ///     the spec only discourages it.
    /// </summary>
    internal static bool IsValidTarget(string? resource) {
        if (string.IsNullOrWhiteSpace(resource)) {
            return false;
        }

        if (!Uri.TryCreate(resource, UriKind.Absolute, out var uri)) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host)) {
            return false;
        }

        if (uri.Fragment.Length > 0) {
            return false;
        }

        return !string.Equals(uri.Scheme, "urn", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
///     Validates the syntax of every <c>resource</c> parameter at the authorization endpoint, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html#section-2">
///         RFC 8707: Resource Indicators for OAuth 2.0 §2: Resource Parameter
///     </seealso>
///     : each value MUST be an absolute URI, MUST NOT include a fragment component. A malformed
///     value rejects the request with <c>invalid_target</c>; an omitted parameter passes through
///     (§2.1 leaves the no-resource policy to the server).
/// </summary>
/// <remarks>
///     Accepted values stay on <see cref="AuthorizeContext{TApplication}.Request" /> and are
///     serialized into the interaction payload; the approval path re-emits them as the
///     <c>Properties.Resources</c> property so the authorization code payload carries the granted set.
/// </remarks>
public sealed class AdviceAuthorizeResource<TApp> : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeResource.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        if (authz.Request?.Resource is not { Count: > 0 }) {
            return Task.FromResult(AdviseResult.Continue);
        }

        foreach (var resource in authz.Request.Resource) {
            if (AdviceAuthorizeResource.IsValidTarget(resource)) {
                continue;
            }

            throw new OAuthException(
                OAuthErrors.InvalidTarget,
                SchemataResources.GetResourceString(SchemataResources.INVALID_TARGET)
            ) {
                RedirectUri  = authz.Request.RedirectUri,
                State        = authz.Request.State,
                ResponseMode = authz.ResponseMode,
            };
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
