using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Filters;
using Schemata.Authorization.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Controllers;

[NoCacheResponse]
[Route("~/Connect")]
[TypeFilter(typeof(OAuthExceptionFilter))]
public partial class ConnectController(IOptions<SchemataAuthorizationOptions> options) : ControllerBase
{
    private Dictionary<string, List<string?>> CollectHeaders() {
        return HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.Select(v => v).ToList());
    }

    private IActionResult MapResult(AuthorizationResult result) {
        return result.Status switch {
            AuthorizationStatus.SignIn when result.Principal is not null => SignIn(
                result.Principal,
                new(result.Properties ?? []),
                result.Properties?.ContainsKey(Properties.ResponseType) == true
                    ? options.Value.CodeScheme
                    : options.Value.BearerScheme),
            AuthorizationStatus.Redirect when !string.IsNullOrWhiteSpace(result.RedirectUri) => Redirect(result.RedirectUri),
            AuthorizationStatus.Content   => new JsonResult(result.Data),
            AuthorizationStatus.Challenge => result.Data is string scheme ? Challenge(scheme) : Challenge(),
            var _                         => throw new NoContentException(),
        };
    }
}
