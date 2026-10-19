using Schemata.Abstractions.Exceptions;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Central response finalization for the authorization endpoint: once the client and redirect
///     have been validated, every failure inherits the captured callback — trusted redirect,
///     preserved state, and the legal effective response mode — so no throw site decorates itself.
/// </summary>
public static class AuthorizationCallbackExtensions
{
    /// <summary>
    ///     Decorates an authorization failure with the validated callback when one exists and the
    ///     failure did not already resolve its own delivery. An absent callback (nothing validated
    ///     yet, or an invalid redirect) stays a bare JSON error — never a guessed callback.
    /// </summary>
    public static OAuthException WithCallback<TApp>(this OAuthException exception, AuthorizeContext<TApp> authz)
        where TApp : Entities.SchemataApplication
    {
        if (authz.Callback is not { } callback || exception.RedirectUri is not null) {
            return exception;
        }

        exception.RedirectUri   = callback.RedirectUri;
        exception.State       ??= callback.State;
        exception.ResponseMode ??= callback.ResponseMode;
        return exception;
    }
}
