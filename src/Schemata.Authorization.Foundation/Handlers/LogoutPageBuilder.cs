using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Schemata.Abstractions;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Renders the front-channel logout page: each relying-party URI as a hidden iframe, with an
///     automatic redirect to the post-logout URI after every iframe finishes loading or a
///     five-second timeout elapses.
/// </summary>
public static class LogoutPageBuilder
{
    /// <summary>
    ///     Builds the logout page HTML for <paramref name="uris" />, redirecting to
    ///     <paramref name="redirect" /> when supplied.
    /// </summary>
    public static string Build(IReadOnlyList<string> uris, string? redirect, CultureInfo? culture = null) {
        var title  = SchemataResources.GetResourceString(SchemataResources.LOGOUT_PAGE_TITLE);
        var text   = SchemataResources.GetResourceString(SchemataResources.LOGOUT_PAGE_TEXT);
        var cont   = SchemataResources.GetResourceString(SchemataResources.LOGOUT_PAGE_CONTINUE);
        var lang   = culture?.Name;
        var encode = (string? value) => WebUtility.HtmlEncode(value);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.Append("<html");
        if (!string.IsNullOrEmpty(lang)) {
            sb.Append(" lang=\"").Append(encode(lang)).Append('"');
        }
        sb.Append("><head><title>").Append(encode(title)).Append("</title>");

        if (!string.IsNullOrWhiteSpace(redirect)) {
            sb.Append("<meta http-equiv=\"refresh\" content=\"5;url=");
            sb.Append(encode(redirect));
            sb.Append("\">");
        }

        sb.AppendLine("</head><body>");

        foreach (var uri in uris) {
            sb.Append("<iframe src=\"");
            sb.Append(encode(uri));
            sb.AppendLine("\" style=\"display:none\"></iframe>");
        }

        sb.Append("<p>").Append(encode(text)).Append("</p>");

        if (!string.IsNullOrWhiteSpace(redirect)) {
            sb.Append("<p><a href=\"");
            sb.Append(encode(redirect));
            sb.Append("\">").Append(encode(cont)).Append("</a></p>");

            sb.AppendLine("<script>");
            sb.AppendLine("(function(){");
            sb.AppendLine("var f=document.querySelectorAll('iframe'),d=0,t=f.length;");
            sb.AppendLine("function c(){if(++d>=t)r();}");
            sb.Append("function r(){window.location.href=\"");
            sb.Append(EscapeJs(redirect));
            sb.AppendLine("\";}");
            sb.AppendLine("for(var i=0;i<t;i++){f[i].onload=c;f[i].onerror=c;}");
            sb.AppendLine("setTimeout(r,5000);");
            sb.AppendLine("if(!t)r();");
            sb.AppendLine("})();");
            sb.AppendLine("</script>");
        }

        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string EscapeJs(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}
