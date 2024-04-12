using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Core.Middlewares;

public class HttpExceptionFilter : IActionFilter, IOrderedFilter
{
    public int Order => Constants.Orders.Max;

    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context) {
        if (context.Exception is not HttpException http) {
            return;
        }

        var response = new ErrorResponse {
            ErrorDescription = !string.IsNullOrWhiteSpace(http.Message) ? http.Message : null,
        };

        if (http.Errors is { Count: > 0 }) {
            response.Errors = http.Errors;
        } else {
            response.Error = http.Error;
        }

        context.Result = response.Error is not null || response.Errors is not null || response.ErrorDescription is not null
                ? new ObjectResult(response) { StatusCode = http.StatusCode }
                : new StatusCodeResult(http.StatusCode);

        context.ExceptionHandled = true;
    }
}
