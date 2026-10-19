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
    ///     Decorates an authorization failure with the validated callback facts when the response
    ///     mode has been resolved and the failure did not already resolve its own delivery. Before
    ///     that point (or with an invalid redirect) the failure stays a bare JSON error — never a
    ///     guessed callback.
    /// </summary>
    public static OAuthException WithCallback<TApp>(this OAuthException exception, AuthorizeContext<TApp> authz)
        where TApp : Entities.SchemataApplication
    {
        if (authz.ResponseMode is null || exception.RedirectUri is not null) {
            return exception;
        }

        if (authz.Request is null) {
            return exception;
        }

        exception.RedirectUri   = authz.Request.RedirectUri;
        exception.State       ??= authz.Request.State;
        exception.ResponseMode ??= authz.ResponseMode;
        return exception;
    }
}
