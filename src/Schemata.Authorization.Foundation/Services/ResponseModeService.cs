using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

public static class ResponseModeService
{
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
