using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

internal sealed class OpSessionService : IOpSessionService
{
    private static readonly object SidKey = new();
    private readonly IHttpContextAccessor _accessor;
    private readonly SchemataAuthorizationOptions _authorization;
    private readonly string _opStateCookie;
    private readonly string _sidCookie;
    private readonly IOpSessionTerminator? _terminator;

    public OpSessionService(
        IHttpContextAccessor                   accessor,
        IOptions<SchemataAuthorizationOptions> authorization,
        IOptions<SessionManagementOptions>     session,
        IOpSessionTerminator?                  terminator = null
    ) {
        _accessor      = accessor;
        _authorization = authorization.Value;
        _opStateCookie = session.Value.OpStateCookieName;
        _sidCookie     = $"{_opStateCookie}.sid";
        _terminator    = terminator;
    }

    public Task<string?> IssueAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        var http = _accessor.HttpContext;
        if (http is null) {
            return Task.FromResult<string?>(null);
        }

        var principalSid = principal?.FindFirstValue(_authorization.SessionIdClaimType);
        var storedSid = http.Items.TryGetValue(SidKey, out var current)
            ? current as string
            : http.Request.Cookies[_sidCookie];
        var sid = !string.IsNullOrWhiteSpace(principalSid) ? principalSid : storedSid ?? Mint(16);
        if (!string.Equals(storedSid, sid, StringComparison.Ordinal)) {
            WriteSid(http, sid);
            OpState.Rotate(http, _opStateCookie);
        } else {
            OpState.GetOrCreate(http, _opStateCookie);
        }

        http.Items[SidKey] = sid;
        return Task.FromResult<string?>(sid);
    }

    public async Task InvalidateAsync(
        ClaimsPrincipal? principal,
        string?          subject,
        string?          sessionId,
        CancellationToken ct = default
    ) {
        if (_terminator is not null) {
            await _terminator.TerminateAsync(principal, subject, sessionId, ct);
        }

        var http = _accessor.HttpContext;
        if (http is not null) {
            http.Response.Cookies.Delete(_sidCookie, new CookieOptions {
                Secure   = true,
                SameSite = SameSiteMode.None,
                HttpOnly = true,
                Path     = "/",
            });
            OpState.Rotate(http, _opStateCookie);
        }
    }

    private void WriteSid(HttpContext http, string sid) {
        http.Response.Cookies.Append(_sidCookie, sid, new CookieOptions {
            Secure   = true,
            SameSite = SameSiteMode.None,
            HttpOnly = true,
            Path     = "/",
        });
    }


    private static string Mint(int bytes) {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));
    }
}