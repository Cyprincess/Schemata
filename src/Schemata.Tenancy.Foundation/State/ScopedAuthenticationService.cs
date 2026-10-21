using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Scope-routing facade registered as the public <see cref="IAuthenticationService" />.
///     Each operation selects the owning scope once through
///     <see cref="IAuthenticationScopeProvider" /> and invokes the native
///     <see cref="AuthenticationService" /> registered under the returned key in that scope.
///     The native service and the handlers it activates belong to the selected scope while
///     <see cref="HttpContext.RequestServices" /> stays untouched, so schemes owned by
///     different scopes can operate on one request concurrently.
/// </summary>
public sealed class ScopedAuthenticationService : IAuthenticationService
{
    /// <summary>Key under which the tenancy feature registers the host native service.</summary>
    public const string HostServiceKey = "Schemata.Authentication.Host";

    #region IAuthenticationService Members

    /// <inheritdoc />
    public async Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) {
        var (service, resolved) = await BindAsync(
            context,
            scheme,
            static schemes => schemes.GetDefaultAuthenticateSchemeAsync(),
            "DefaultAuthenticateScheme");

        return await service.AuthenticateAsync(context, resolved);
    }

    /// <inheritdoc />
    public async Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) {
        var (service, resolved) = await BindAsync(
            context,
            scheme,
            static schemes => schemes.GetDefaultChallengeSchemeAsync(),
            "DefaultChallengeScheme");

        await service.ChallengeAsync(context, resolved, properties);
    }

    /// <inheritdoc />
    public async Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) {
        var (service, resolved) = await BindAsync(
            context,
            scheme,
            static schemes => schemes.GetDefaultForbidSchemeAsync(),
            "DefaultForbidScheme");

        await service.ForbidAsync(context, resolved, properties);
    }

    /// <inheritdoc />
    public async Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) {
        var (service, resolved) = await BindAsync(
            context,
            scheme,
            static schemes => schemes.GetDefaultSignInSchemeAsync(),
            "DefaultSignInScheme");

        await service.SignInAsync(context, resolved, principal, properties);
    }

    /// <inheritdoc />
    public async Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) {
        var (service, resolved) = await BindAsync(
            context,
            scheme,
            static schemes => schemes.GetDefaultSignOutSchemeAsync(),
            "DefaultSignOutScheme");

        await service.SignOutAsync(context, resolved, properties);
    }

    #endregion

    private static async Task<(IAuthenticationService Service, string Scheme)> BindAsync(
        HttpContext                                                      context,
        string?                                                          scheme,
        Func<IAuthenticationSchemeProvider, Task<AuthenticationScheme?>> resolveDefault,
        string                                                           slot
    ) {
        if (scheme is null) {
            scheme = (await resolveDefault(context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>()))?.Name
                ?? throw new InvalidOperationException(MissingDefault(slot));
        }

        var scope = context.RequestServices.GetService<IAuthenticationScopeProvider>()?.GetScope(context, scheme)
            ?? new AuthenticationScope(context.RequestServices, HostServiceKey);
        var service = scope.Services.GetKeyedService<IAuthenticationService>(scope.ServiceKey)
            ?? throw new InvalidOperationException(
                $"No native {nameof(IAuthenticationService)} is registered under key '{scope.ServiceKey}' for authentication scheme '{scheme}'.");

        return (service, scheme);
    }

    private static string MissingDefault(string slot) {
        return $"No authenticationScheme was specified, and there was no {slot} found. "
             + "The default schemes can be set using either AddAuthentication(string defaultScheme) or AddAuthentication(Action<AuthenticationOptions> configureOptions).";
    }
}
