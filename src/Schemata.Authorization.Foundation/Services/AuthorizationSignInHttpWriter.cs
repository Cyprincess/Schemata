using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Schemata.Authorization.Foundation.Services;

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
            await action.ExecuteResultAsync(new(context, context.GetRouteData(), new()));
        }
    }
}
