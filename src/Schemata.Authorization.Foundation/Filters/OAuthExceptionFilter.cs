using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Globalization;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Filters;

/// <summary>Converts OAuth exceptions into redirect callbacks or JSON error responses.</summary>
public sealed class OAuthExceptionFilter(IOptions<SchemataAuthorizationOptions> options) : IExceptionFilter
{
    #region IExceptionFilter Members

    public void OnException(ExceptionContext context) {
        if (context.Exception is not OAuthException oauth) return;

        // RFC 9449 §8/§9: a nonce challenge only reaches the client through the rendered
        // response's DPoP-Nonce header, so header state attached by the thrower rides
        // both the redirect and the JSON transports.
        if (oauth.Headers is { Count: > 0 }) {
            foreach (var pair in oauth.Headers) {
                context.HttpContext.Response.Headers[pair.Key] = pair.Value;
            }
        }

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
            if (oauth is { Code: (int)HttpStatusCode.Unauthorized, Status: OAuthErrors.InvalidClient }) {
                context.HttpContext.Response.Headers.WWWAuthenticate = Schemes.Basic;
            }

            var locale = AcceptLanguageParser.Parse(context.HttpContext.Request.Headers.AcceptLanguage)?.Name;
            context.Result = new JsonResult(oauth.CreateErrorResponse(context.HttpContext.TraceIdentifier, locale: locale)) { StatusCode = oauth.Code };
        }

        context.ExceptionHandled = true;
    }

    #endregion
}
