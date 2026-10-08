using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace Schemata.Authorization.Foundation.Filters;

/// <summary>
///     Per-action embedding policy for the OP check-session iframe. RPs embed this endpoint
///     cross-origin per OIDC Session Management §3.2, so the class-wide DENY / frame-ancestors
///     'self' clickjacking protection is replaced here only; every other Connect page keeps it.
///     The iframe's script still answers postMessage only for expected origins, so an unknown
///     embedder learns nothing about the session state.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CheckSessionFrameAttribute : Attribute, IResultFilter
{
    #region IResultFilter Members

    public void OnResultExecuting(ResultExecutingContext context) {
        // Action-scope filters run after the controller-scope NoCacheResponse filter, so these
        // writes replace its frame headers for this endpoint only; the cache-control headers it
        // set stay in place.
        var headers = context.HttpContext.Response.Headers;
        headers.XFrameOptions        = StringValues.Empty;
        headers.ContentSecurityPolicy = "frame-ancestors *";
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    #endregion
}
