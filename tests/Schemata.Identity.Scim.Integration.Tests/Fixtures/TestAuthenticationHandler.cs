using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Schemata.Identity.Scim.Integration.Tests.Fixtures;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory                               logger,
    UrlEncoder                                   encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ScimTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        if (!Request.Headers.ContainsKey("X-Test-Scim")) {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var domain = Request.Headers["X-Test-Domain"].FirstOrDefault() ?? "domain-a";
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.Name, "provisioner"),
            new("scim_domain", domain),
        ], SchemeName));

        return Task.FromResult(AuthenticateResult.Success(new(principal, SchemeName)));
    }
}
