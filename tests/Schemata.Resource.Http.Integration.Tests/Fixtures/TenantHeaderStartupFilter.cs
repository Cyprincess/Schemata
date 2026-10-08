using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Schemata.Abstractions.Tenancy;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

/// <summary>
///     Enters <see cref="TenantContext" /> from the <c>X-Test-Tenant</c> header ahead of the
///     Schemata pipeline so continuation binding observes a per-request tenant scope.
/// </summary>
internal sealed class TenantHeaderStartupFilter : IStartupFilter
{
    #region IStartupFilter Members

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
        app.Use(async (http, proceed) => {
            if (http.Request.Headers.TryGetValue("X-Test-Tenant", out var raw) && Guid.TryParse(raw, out var uid)) {
                using var frame = TenantContext.Enter(new(uid));
                await proceed();
                return;
            }

            await proceed();
        });
        next(app);
    };

    #endregion
}
