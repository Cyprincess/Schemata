using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

/// <summary>
///     Authenticates when the request carries the header <c>X-Test-Auth: valid</c>; otherwise
///     yields no result so the protected endpoint challenges with 401.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory                                  logger,
    UrlEncoder                                      encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TestScheme = nameof(TestScheme);

    #region AuthenticationHandler<AuthenticationSchemeOptions> Members

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (Context.Request.Headers["X-Test-Auth"] == "valid"
         && (!Context.Request.Headers.TryGetValue("X-Test-Scheme", out var requested) || requested == Scheme.Name)) {
            var identity = new System.Security.Claims.ClaimsIdentity(Scheme.Name);
            if (Context.Request.Headers.TryGetValue("X-Test-Subject", out var subject) && !string.IsNullOrWhiteSpace(subject)) {
                identity.AddClaim(new(System.Security.Claims.ClaimTypes.NameIdentifier, subject.ToString()));
            }

            var principal = new System.Security.Claims.ClaimsPrincipal(identity);
            var ticket    = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    #endregion
}
