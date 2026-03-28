using System;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Schemata.Authorization.Foundation.Filters;

[AttributeUsage(AttributeTargets.Class)]
public sealed class NoCacheResponseAttribute : Attribute, IResultFilter
{
    #region IResultFilter Members

    public void OnResultExecuting(ResultExecutingContext context) {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma       = "no-cache";
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    #endregion
}
