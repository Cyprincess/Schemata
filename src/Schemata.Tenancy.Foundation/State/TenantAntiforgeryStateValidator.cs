using System;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.State;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Requires the host antiforgery registration before the state branch provider is created, so
///     a tenant antiforgery branch never runs without the global branch it excludes per request.
/// </summary>
internal sealed class TenantAntiforgeryStateValidator(TenantStateProfile profile, IServiceProvider host) : IStatePipelineValidator
{
    public void Validate() {
        if (host.GetService<IAntiforgery>() is not null) return;

        throw new InvalidOperationException(
            $"Tenant state for '{profile.TenantUid:N}' declares an antiforgery capability, but the host application registered no antiforgery services; "
          + "enable the global antiforgery feature (AddAntiforgery with UseStatePipeline(Antiforgery)) so tenant and global branches stay mutually exclusive per request.");
    }
}
