using System.Threading.Tasks;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Globalization;

namespace Schemata.Transport.Http.Middleware;

/// <summary>
///     Flows the <c>Accept-Language</c> preference into the request's
///     <see cref="CultureInfo.CurrentCulture" /> and
///     <see cref="CultureInfo.CurrentUICulture" /> so resx lookups, FluentValidation
///     messages, and <c>IdentityErrorDescriber</c> texts all resolve in the caller's
///     locale for the duration of the request.
/// </summary>
public sealed class RequestCultureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context) {
        var culture = AcceptLanguageParser.Parse(context.Request.Headers.AcceptLanguage);
        if (culture is not null) {
            CultureInfo.CurrentCulture   = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        await next(context);
    }
}
