using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Schemata.Flow.Integration.Tests.Resource.Fixtures;

public sealed class ParticipantAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Participant";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        var subject = Request.Headers["X-Subject"].ToString();
        if (string.IsNullOrEmpty(subject)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(SchemeName);
        identity.AddClaim(new("sub", subject));
        foreach (var permission in Request.Headers["X-Permissions"].ToString().Split(',', System.StringSplitOptions.RemoveEmptyEntries)) {
            identity.AddClaim(new("permission", permission));
        }
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(identity), SchemeName)));
    }
}
