using System;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Schemata.Core.Filters;

/// <summary>
///     Applies no-store cache headers plus clickjacking guards to MVC results, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-4.16">
///         RFC 9700: Best Current Practice for OAuth 2.0 Security §4.16: Clickjacking
///     </seealso>
///     .
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoCacheResponseAttribute : Attribute, IResultFilter
{
    #region IResultFilter Members

    public void OnResultExecuting(ResultExecutingContext context) {
        context.HttpContext.Response.Headers.CacheControl          = "no-store";
        context.HttpContext.Response.Headers.Pragma                = "no-cache";
        context.HttpContext.Response.Headers.XFrameOptions          = "DENY";
        context.HttpContext.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'self'";
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    #endregion
}