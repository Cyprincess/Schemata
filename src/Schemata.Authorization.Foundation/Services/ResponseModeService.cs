using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Resolves and builds authorization response callbacks.
///     Supports three modes:
///     <list type="bullet">
///         <item>
///             <description><c>query</c> — parameters appended to the redirect URI query string.</description>
///         </item>
///         <item>
///             <description>
///                 <c>fragment</c> — parameters appended as a URI fragment (used when tokens are returned in the
///                 front channel).
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>form_post</c> — HTML page with an auto-submitting form (more secure, avoids Referer
///                 leakage).
///             </description>
///         </item>
///     </list>
///     per
///     <seealso href="https://openid.net/specs/oauth-v2-multiple-response-types-1_0.html">
///         OAuth 2.0 Multiple Response Type
///         Encoding Practices 1.0
///     </seealso>
///     .
/// </summary>
public static class ResponseModeService
{
    /// <summary>
    ///     Resolves the response_mode from the explicit value or infers it
    ///     from the response_type.  When response_type includes <c>token</c>
    ///     or <c>id_token</c>, <c>fragment</c> is used; otherwise <c>query</c>.
    /// </summary>
    /// <param name="responseMode">Explicit response_mode value, or null to infer.</param>
    /// <param name="responseType">The response_type value used for inference.</param>
    public static string ResolveMode(string? responseMode, string? responseType) {
        if (!string.IsNullOrWhiteSpace(responseMode)) {
            return responseMode;
        }

        if (string.IsNullOrWhiteSpace(responseType)) {
            return ResponseModes.Query;
        }

        return responseType.Contains(ResponseTypes.Token) || responseType.Contains(ResponseTypes.IdToken)
            ? ResponseModes.Fragment
            : ResponseModes.Query;
    }

    /// <summary>
    ///     Creates an <see cref="IActionResult" /> that delivers the authorization
    ///     response to the redirect URI using the specified mode.
    /// </summary>
    /// <param name="redirectUri">The validated redirect URI.</param>
    /// <param name="parameters">Response parameters (code, token, state, etc.).</param>
    /// <param name="responseMode">The delivery mode: "query", "fragment", or "form_post".</param>
    public static IActionResult CreateCallback(
        string                      redirectUri,
        Dictionary<string, string?> parameters,
        string                      responseMode
    ) {
        return responseMode switch {
            ResponseModes.Query    => CreateQueryRedirect(redirectUri, parameters),
            ResponseModes.Fragment => CreateFragmentRedirect(redirectUri, parameters),
            ResponseModes.FormPost => CreateFormPost(redirectUri, parameters),
            var _                  => CreateQueryRedirect(redirectUri, parameters),
        };
    }

    private static IActionResult CreateQueryRedirect(string redirectUri, Dictionary<string, string?> parameters) {
        var separator = redirectUri.Contains('?') ? '&' : '?';
        var query = string.Join("&", parameters.Where(p => p.Value is not null)
                                               .Select(p => $"{
                                                   Uri.EscapeDataString(p.Key)
                                               }={
                                                   Uri.EscapeDataString(p.Value!)
                                               }"));
        return new RedirectResult($"{redirectUri}{separator}{query}");
    }

    private static IActionResult CreateFragmentRedirect(string redirectUri, Dictionary<string, string?> parameters) {
        var fragment = string.Join("&", parameters.Where(p => p.Value is not null)
                                                  .Select(p => $"{
                                                      Uri.EscapeDataString(p.Key)
                                                  }={
                                                      Uri.EscapeDataString(p.Value!)
                                                  }"));
        return new RedirectResult($"{redirectUri}#{fragment}");
    }

    /// <summary>
    ///     Creates an HTML page with a self-submitting form that POSTs the
    ///     authorization response parameters to the redirect URI.
    ///     Safer than query/fragment modes because the response data is not
    ///     exposed in the Referer header or browser history.
    /// </summary>
    private static IActionResult CreateFormPost(string redirectUri, Dictionary<string, string?> parameters) {
        var fields = string.Join(
            "\n",
            parameters.Where(p => p.Value is not null)
                      .Select(p => $"<input type=\"hidden\" name=\"{
                          WebUtility.HtmlEncode(p.Key)
                      }\" value=\"{
                          WebUtility.HtmlEncode(p.Value!)
                      }\" />"));

        var html = $@"<!DOCTYPE html>
<html><head><title>Submit</title></head>
<body onload=""document.forms[0].submit()"">
<form method=""post"" action=""{
    WebUtility.HtmlEncode(redirectUri)
}"">
{
    fields
}
<noscript><button type=""submit"">Continue</button></noscript>
</form></body></html>";

        return new ContentResult { Content = html, ContentType = MediaTypeNames.Text.Html, StatusCode = 200 };
    }
}
