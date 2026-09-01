using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Writes an issued authorization response for compatibility authentication-scheme calls.</summary>
public interface IAuthorizationSignInHttpWriter
{
    Task WriteAsync(
        HttpContext                 context,
        AuthorizationSignInResponse response,
        CancellationToken           ct = default);
}

internal sealed class AuthorizationSignInHttpWriter(IOptions<JsonSerializerOptions> json)
    : IAuthorizationSignInHttpWriter
{
    public async Task WriteAsync(
        HttpContext                 context,
        AuthorizationSignInResponse response,
        CancellationToken           ct = default
    ) {
        if (response.Token is not null) {
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await JsonSerializer.SerializeAsync(context.Response.Body, response.Token, json.Value, ct);
            return;
        }

        if (response.Callback is not null) {
            var action = ResponseModeService.CreateCallback(
                response.Callback.RedirectUri,
                response.Callback.Parameters,
                response.Callback.ResponseMode!);
            await action.ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new()));
        }
    }
}
