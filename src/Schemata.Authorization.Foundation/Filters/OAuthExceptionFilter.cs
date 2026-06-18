using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Filters;

public sealed class OAuthExceptionFilter(IOptions<SchemataAuthorizationOptions> options) : IExceptionFilter
{
    #region IExceptionFilter Members

    public void OnException(ExceptionContext context) {
        if (context.Exception is not OAuthException oauth) return;

        if (oauth.RedirectUri is not null) {
            var parameters = new Dictionary<string, string?> {
                [Parameters.Error]            = oauth.Status,
                [Parameters.ErrorDescription] = oauth.Message,
                [Parameters.ErrorUri]         = oauth.ErrorUri,
                [Parameters.State]            = oauth.State,
            };

            if (!string.IsNullOrWhiteSpace(options.Value.Issuer)) {
                parameters[Claims.Issuer] = options.Value.Issuer;
            }

            context.Result = ResponseModeService.CreateCallback(
                oauth.RedirectUri,
                parameters.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value),
                oauth.ResponseMode ?? ResponseModes.Query
            );
        } else {
            // RFC 6749 §5.2: an invalid_client error answered with HTTP 401 MUST carry a WWW-Authenticate
            // challenge naming the scheme the client used; Basic is the scheme this server accepts on the
            // Authorization header.
            if (oauth.Code == (int)HttpStatusCode.Unauthorized && oauth.Status == OAuthErrors.InvalidClient) {
                context.HttpContext.Response.Headers.WWWAuthenticate = Schemes.Basic;
            }

            context.Result = new JsonResult(oauth.CreateErrorResponse(context.HttpContext.TraceIdentifier)) { StatusCode = oauth.Code };
        }

        context.ExceptionHandled = true;
    }

    #endregion
}
