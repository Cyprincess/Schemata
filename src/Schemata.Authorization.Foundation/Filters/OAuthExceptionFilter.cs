using System.Collections.Generic;
using System.Linq;
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
                [Parameters.Error]            = oauth.Code,
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
            context.Result = new JsonResult(oauth.CreateErrorResponse()) { StatusCode = oauth.Status };
        }

        context.ExceptionHandled = true;
    }

    #endregion
}
