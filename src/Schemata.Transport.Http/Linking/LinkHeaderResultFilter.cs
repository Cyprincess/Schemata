using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Core.Building;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     The single MVC result filter that collects every <see cref="ILinkContributor" /> link and
///     writes one RFC 8288 <c>Link</c> header field. Components register contributors; they never
///     build the field themselves.
/// </summary>
public sealed class LinkHeaderResultFilter : IAlwaysRunResultFilter
{
    #region IAlwaysRunResultFilter Members

    public void OnResultExecuting(ResultExecutingContext context) {
        if (!IsSuccess(context.Result)) {
            return;
        }

        var resource = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ResourceRegistration>();
        var state = new LinkContext(context.HttpContext, context.Result, resource);

        var links = new List<WebLink>();
        foreach (var contributor in context.HttpContext.RequestServices.GetServices<ILinkContributor>()) {
            var contributed = contributor.GetLinks(state);
            if (contributed is null) {
                continue;
            }

            links.AddRange(contributed);
        }

        if (links.Count == 0) {
            return;
        }

        var value = Serialize(links);
        if (value.Length == 0) {
            return;
        }

        context.HttpContext.Response.Headers[LinkHeader.Name] = value;
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    #endregion

    private static string Serialize(List<WebLink> links) {
        try {
            return LinkHeader.Serialize(links);
        } catch (ArgumentException) {
            // Links are ancillary response metadata; an invalid contributed target must not turn
            // an otherwise successful response into a server error.
            return string.Empty;
        }
    }

    private static bool IsSuccess(IActionResult result) => result switch {
        ChallengeResult or ForbidResult         => false,
        StatusCodeResult status                 => status.StatusCode < 400,
        ObjectResult { StatusCode: { } status } => status < 400,
        JsonResult { StatusCode: { } status }   => status < 400,
        _                                       => true,
    };
}

internal sealed class LinkHeaderMvcOptionsSetup : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) {
        if (options.Filters.Any(filter => filter is LinkHeaderResultFilter)) {
            return;
        }

        options.Filters.Add(new LinkHeaderResultFilter());
    }
}
