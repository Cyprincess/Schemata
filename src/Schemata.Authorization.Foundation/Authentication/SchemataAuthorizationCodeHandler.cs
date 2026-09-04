using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     Compatibility authentication-scheme adapter. Protocol issuance is transport-neutral in
///     <see cref="IAuthorizationSignInService" />; this type only writes the issued response when an
///     application invokes the legacy authorization-code sign-in scheme directly.
/// </summary>
public class SchemataAuthorizationCodeHandler<TApp>(
    IOptionsMonitor<SchemataAuthenticationHandlerOptions> options,
    ILoggerFactory                                        logger,
    UrlEncoder                                            encoder,
    IAuthorizationSignInService                           signIns,
    IAuthorizationSignInHttpWriter                        writer
) : SignInAuthenticationHandler<SchemataAuthenticationHandlerOptions>(options, logger, encoder)
    where TApp : SchemataApplication
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        throw new NotImplementedException();
    }

    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) {
        throw new NotImplementedException();
    }

    protected override async Task HandleSignInAsync(
        ClaimsPrincipal          principal,
        AuthenticationProperties? properties
    ) {
        var response = await signIns.IssueAsync(
            principal, properties?.Items, AuthorizationSignInResponseKind.Callback, Context.RequestAborted);
        await writer.WriteAsync(Context, response, Context.RequestAborted);
    }
}
