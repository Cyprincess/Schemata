using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Schemata.Identity.Foundation.Runtime;

/// <summary>
///     Sends a browser without a cookie session to <see cref="SchemataIdentityOptions.LoginUri" />,
///     carrying the original local path in a Data Protection payload. The payload is opaque and
///     tamper-evident, so the login page cannot be used as an open redirect.
/// </summary>
internal static class LoginContinuation
{
    /// <summary>Data Protection purpose isolating continuation payloads from every other protector.</summary>
    public const string Purpose = "Schemata.Identity.Continue";

    /// <summary>Query parameter carrying the protected continuation payload.</summary>
    public const string Parameter = "continue";

    public static Task RedirectToLoginAsync(RedirectContext<CookieAuthenticationOptions> context) {
        var services = context.HttpContext.RequestServices;
        var login    = services.GetRequiredService<IOptionsMonitor<SchemataIdentityOptions>>().CurrentValue.LoginUri;

        if (string.IsNullOrWhiteSpace(login)) {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        var request = context.Request;
        var target  = $"{request.PathBase}{request.Path}{request.QueryString}";

        context.Response.Redirect(
            QueryHelpers.AddQueryString(login!, Parameter, Protector(services).Protect(target)));

        return Task.CompletedTask;
    }

    public static IDataProtector Protector(IServiceProvider services) {
        return services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
    }
}
