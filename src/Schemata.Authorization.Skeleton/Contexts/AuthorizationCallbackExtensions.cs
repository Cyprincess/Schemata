using Schemata.Abstractions.Exceptions;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Central response finalization for the authorization endpoint: once the client and redirect
///     have been validated, every failure inherits the validated redirect, preserved state, and
///     effective response mode from the single request object, so no throw site decorates itself.
/// </summary>
public static class AuthorizationCallbackExtensions
{
    /// <summary>
    ///     Decorates an authorization failure with missing callback facts after client and redirect
    ///     validation. Existing redirect and state values are preserved, and the validated context
    ///     supplies a missing response mode.
    /// </summary>
    public static OAuthException WithCallback<TApp>(this OAuthException exception, AuthorizeContext<TApp> authz)
        where TApp : Entities.SchemataApplication
    {
        if (authz.ResponseMode is not { } mode
         || authz.Request is not { RedirectUri: { } redirectUri } request) {
            return exception;
        }

        exception.RedirectUri ??= redirectUri;
        exception.State       ??= request.State;
        exception.ResponseMode ??= mode;
        return exception;
    }
}
