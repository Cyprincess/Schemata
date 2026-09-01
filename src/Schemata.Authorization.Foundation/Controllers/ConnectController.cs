using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Filters;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Controllers;

/// <summary>
///     Hosts OAuth 2.0 and OpenID Connect endpoints under <c>/Connect</c>.
/// </summary>
[NoCacheResponse]
[Route("~/Connect")]
[TypeFilter(typeof(OAuthExceptionFilter))]
public partial class ConnectController(
    IOptions<SchemataAuthorizationOptions> options,
    IAuthorizationSignInService            signIns,
    IOptions<JsonSerializerOptions>        json,
    IRequestDispatcher                    dispatcher
) : ControllerBase
{
    private Dictionary<string, List<string?>> CollectHeaders() {
        return HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.Select(v => v).ToList());
    }

    private async Task<IActionResult> MapResult(
        AuthorizationResult result,
        CancellationToken   ct
    ) {
        if (result.Status == AuthorizationStatus.SignIn && result.Principal is not null) {
            var kind = result.Properties?.ContainsKey(Properties.ResponseType) == true
                ? AuthorizationSignInResponseKind.Callback
                : AuthorizationSignInResponseKind.Token;
            var issued = await signIns.IssueAsync(result.Principal, result.Properties, kind, ct);
            if (issued.Token is not null) {
                return new JsonResult(issued.Token, json.Value);
            }

            if (issued.Callback is not null) {
                return ResponseModeService.CreateCallback(
                    issued.Callback.RedirectUri,
                    issued.Callback.Parameters,
                    issued.Callback.ResponseMode!);
            }

            throw new NoContentException();
        }

        return result.Status switch {
            AuthorizationStatus.Redirect when !string.IsNullOrWhiteSpace(result.RedirectUri) =>
                Redirect(result.RedirectUri),
            AuthorizationStatus.Content   => new JsonResult(result.Data),
            AuthorizationStatus.Challenge => result.Data is string scheme ? Challenge(scheme) : Challenge(),
            var _                         => throw new NoContentException(),
        };
    }
}
