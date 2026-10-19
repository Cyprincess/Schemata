using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Scim.Middleware;

internal sealed class ScimErrorMiddleware(
    RequestDelegate                 next,
    ILogger<ScimErrorMiddleware>    logger
)
{
    public async Task InvokeAsync(HttpContext context) {
        if (!ScimResults.IsScimPath(context.Request.Path)) {
            await next(context);
            return;
        }

        try {
            await next(context);
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
            throw;
        } catch (Exception error) {
            if (context.Response.HasStarted) {
                throw;
            }

            var status = error is SchemataException { Code: >= 400 and <= 599 } failure ? failure.Code : 500;
            var detail = error is SchemataException and { Message.Length: > 0 } ? error.Message : "An internal error occurred.";

            logger.LogError(error, "SCIM request failed with status {Status}.", status);
            await ScimResults.WriteErrorAsync(context.Response, status, detail: detail);
            return;
        }

        if (context.Response.HasStarted) {
            return;
        }

        switch (context.Response.StatusCode) {
            case StatusCodes.Status404NotFound:
                await ScimResults.WriteErrorAsync(context.Response, StatusCodes.Status404NotFound, detail: "The SCIM endpoint was not found.");
                break;
            case StatusCodes.Status405MethodNotAllowed:
                await ScimResults.WriteErrorAsync(context.Response, StatusCodes.Status405MethodNotAllowed, detail: "The HTTP method is not supported for this SCIM endpoint.");
                break;
            case StatusCodes.Status415UnsupportedMediaType:
                await ScimResults.WriteErrorAsync(context.Response, StatusCodes.Status415UnsupportedMediaType,
                                                  ScimConstants.InvalidSyntax, ScimConstants.UnsupportedMediaTypeDetail);
                break;
        }
    }
}
