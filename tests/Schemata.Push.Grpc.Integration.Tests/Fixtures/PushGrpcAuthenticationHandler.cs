using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Schemata.Push.Grpc.Integration.Tests.Fixtures;

public sealed class PushGrpcAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        var subject = Request.Headers["X-Owner"].ToString();
        if (string.IsNullOrEmpty(subject)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new Claim("sub", subject));
        foreach (var permission in Request.Headers["X-Permissions"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)) {
            identity.AddClaim(new Claim("permission", permission));
        }
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), SchemeName)));
    }
}
