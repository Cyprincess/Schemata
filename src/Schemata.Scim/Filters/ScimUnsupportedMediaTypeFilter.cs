using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Schemata.Scim.Filters;

internal sealed class ScimUnsupportedMediaTypeSetup : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) => options.Filters.Add(new ScimUnsupportedMediaTypeFilter());
}

/// <summary>
///     Replaces MVC's unsupported-media-type result on the SCIM path with a SCIM error before the
///     client-error ProblemDetails response executes.
/// </summary>
internal sealed class ScimUnsupportedMediaTypeFilter : IAlwaysRunResultFilter, IOrderedFilter
{
    public int Order => -10_000;

    public void OnResultExecuting(ResultExecutingContext context) {
        if (!ScimResults.IsScimPath(context.HttpContext.Request.Path)) {
            return;
        }

        var status = context.Result switch {
            StatusCodeResult result                             => result.StatusCode,
            ObjectResult { StatusCode: { } code }               => code,
            ObjectResult { Value: ProblemDetails { Status: { } value } } => value,
            _                                                   => 0,
        };
        if (status != StatusCodes.Status415UnsupportedMediaType) {
            return;
        }

        context.Result = ScimResults.Error(StatusCodes.Status415UnsupportedMediaType,
                                           ScimConstants.InvalidSyntax,
                                           ScimConstants.UnsupportedMediaTypeDetail);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
