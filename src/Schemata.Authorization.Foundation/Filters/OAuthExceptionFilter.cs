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

/// <summary>Converts OAuth exceptions into redirect callbacks or JSON error responses.</summary>
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

            var locale = ParseAcceptLanguage(context.HttpContext.Request.Headers.AcceptLanguage);
            context.Result = new JsonResult(oauth.CreateErrorResponse(context.HttpContext.TraceIdentifier, locale: locale)) { StatusCode = oauth.Code };
        }

        context.ExceptionHandled = true;
    }

    /// <summary>
    ///     Extracts the highest-quality language tag from an <c>Accept-Language</c> header
    ///     (e.g. <c>"zh-CN,en-US;q=0.9"</c> -> <c>"zh-CN"</c>). Returns <see langword="null" />
    ///     when the header is empty so the central <c>EnsureLocalizedMessage</c> helper skips
    ///     localization.
    /// </summary>
    private static string? ParseAcceptLanguage(Microsoft.Extensions.Primitives.StringValues header) {
        foreach (var value in header) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            foreach (var segment in value.Split(',')) {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0) {
                    continue;
                }

                var semicolon = trimmed.IndexOf(';');
                var tag       = semicolon < 0 ? trimmed : trimmed[..semicolon].Trim();
                if (tag.Length == 0 || tag == "*") {
                    continue;
                }

                return tag;
            }
        }

        return null;
    }

    #endregion
}
