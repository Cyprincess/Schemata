using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;

namespace Schemata.Authorization.Foundation.Handlers;

public sealed class SessionManagementHandler<TApp>(
    IApplicationManager<TApp>              apps,
    IOptions<SessionManagementOptions> options
) : SessionManagementEndpoint where TApp : SchemataApplication
{
    public override async Task<string> CheckSessionAsync(CancellationToken ct) {
        var origins = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await foreach (var app in apps.ListAsync(null, ct)) {
            if (string.IsNullOrWhiteSpace(app.ClientId)) {
                continue;
            }

            var registered = app.RedirectUris?
                .Select(OriginOf)
                .Where(origin => origin is not null)
                .Select(origin => origin!)
                .ToHashSet(StringComparer.Ordinal) ?? [];
            if (registered.Count > 0) {
                origins[app.ClientId] = registered;
            }
        }

        return BuildHtml(JsonSerializer.Serialize(origins), options.Value.OpStateCookieName);
    }

    private static string BuildHtml(string origins, string cookieName) {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><title>OP Check Session</title></head><body><script>");
        html.Append("(function(){'use strict';");
        html.Append("const ORIGINS=").Append(origins).Append(';');
        html.Append("const COOKIE_NAME=").Append(JsonSerializer.Serialize(cookieName)).Append(';');
        html.Append("function readCookie(n){const p=document.cookie.split('; ').find(v=>v.startsWith(n+'='));return p?decodeURIComponent(p.slice(n.length+1)):'';}");
        html.Append("function b64u(bytes){let s='';for(const b of bytes)s+=String.fromCharCode(b);return btoa(s).replace(/\\+/g,'-').replace(/\\//g,'_').replace(/=+$/,'');}");
        html.Append("async function hash(s){const d=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(s));return b64u(new Uint8Array(d));}");
        html.Append("function reply(e,v){if(e.source)e.source.postMessage(v,e.origin);}");
        html.Append("window.addEventListener('message',async function(e){");
        html.Append("if(typeof e.data!=='string'){reply(e,'error');return;}");
        html.Append("const p=e.data.split(' ');if(p.length!==2||!p[0]||!p[1]){reply(e,'error');return;}");
        html.Append("const allowed=ORIGINS[p[0]];if(!allowed||!allowed.includes(e.origin)){reply(e,'error');return;}");
        html.Append("const state=p[1].split('.');if(state.length!==2||!state[0]||!state[1]){reply(e,'error');return;}");
        html.Append("const actual=await hash(p[0]+' '+e.origin+' '+readCookie(COOKIE_NAME)+' '+state[1]);");
        html.Append("reply(e,actual===state[0]?'unchanged':'changed');");
        html.Append("});})();</script></body></html>");
        return html.ToString();
    }

    private static string? OriginOf(string uri) {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.GetLeftPart(UriPartial.Authority)
            : null;
    }
}