using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Core.Middlewares;

public class HttpExceptionFilter : IActionFilter, IOrderedFilter
{
    #region IActionFilter Members

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

    #endregion

    #region IOrderedFilter Members

    public int Order => SchemataConstants.Orders.Max;

    #endregion
}
