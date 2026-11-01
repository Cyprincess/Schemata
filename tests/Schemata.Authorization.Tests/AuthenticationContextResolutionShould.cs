using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Behavioral coverage for <see cref="AuthenticationContextExtensions.ResolveAsync" /> when
///     an approval must resolve its own event. Caller-vetted continuation inheritance is selected
///     by the interaction handler before this resolver is invoked.
/// </summary>
public class AuthenticationContextResolutionShould
{
    [Fact]
    public async Task Resolve_The_Principal_Event_When_The_Same_Session_Reauthenticates() {
        AuthenticationContext? inherited = null;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
            new(Claims.Acr, "urn:example:acr:password"),
            new(Claims.Amr, "pwd"),
            new(Claims.AuthTime, "1700001000"),
        ], "test"));

        var resolved = await AuthenticationContextExtensions.ResolveAsync(
            principal, inherited, "sid-1", Claims.SessionId, null, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("urn:example:acr:password", resolved.Acr);
        Assert.Equal(["pwd"], resolved.Amr);
        Assert.Equal(1700001000, resolved.AuthTime);
    }

    [Fact]
    public async Task Resolve_Provider_Evidence() {
        AuthenticationContext? inherited = null;
        var fresh     = new AuthenticationContext("urn:example:acr:password", new List<string> { "pwd" }, 1700001000);
        var provider  = new FixedContextProvider(fresh);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
        ], "test"));

        var resolved = await AuthenticationContextExtensions.ResolveAsync(
            principal, inherited, "sid-1", Claims.SessionId, provider, CancellationToken.None);

        Assert.Equal(fresh, resolved);
    }

    [Fact]
    public async Task Inherit_The_Continuation_When_No_Fresh_Evidence_Exists() {
        var inherited = new AuthenticationContext("urn:example:acr:mfa", ["mfa"], 1700000000);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/u-1"),
        ], "test"));

        var resolved = await AuthenticationContextExtensions.ResolveAsync(
            principal, inherited, "sid-1", Claims.SessionId, null, CancellationToken.None);

        Assert.Equal(inherited, resolved);
    }

    [Fact]
    public async Task Return_No_Context_When_Neither_Fresh_Nor_Inherited_Evidence_Exists() {
        var resolved = await AuthenticationContextExtensions.ResolveAsync(
            null, null, null, Claims.SessionId, null, CancellationToken.None);

        Assert.Null(resolved);
    }

    private sealed class FixedContextProvider(AuthenticationContext context) : IAuthenticationContextProvider
    {
        public Task<AuthenticationContext> GetContextAsync(ClaimsPrincipal? principal, CancellationToken ct = default) {
            return Task.FromResult(context);
        }
    }
}
