using System.Linq;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Tenancy.Skeleton.Tenancy;

namespace Schemata.Tenancy.Foundation.Services;

internal sealed class TenantQuotaOptions : IPostConfigureOptions<RateLimiterOptions>
{
    public void PostConfigure(string? name, RateLimiterOptions options) {
        if (options.OnRejected is not { } rejected) return;
        options.OnRejected = async (context, ct) => {
            var supplied = context.Lease.TryGetMetadata(QuotaMetadata.Subject, out var subject)
                        && !string.IsNullOrWhiteSpace(subject);
            try {
                await rejected(context, ct);
            } catch (QuotaExceededException error) when (!supplied) {
                var identity = context.HttpContext.RequestServices.GetRequiredService<TenantIdentity>();
                if (identity.Uid is not { } uid) throw;
                if (error.Details is { } details) {
                    foreach (var detail in details.OfType<QuotaFailureDetail>()) {
                        if (detail.Violations is not { } violations) {
                            continue;
                        }

                        foreach (var violation in violations) {
                            violation.Subject = $"tenants/{uid:D}/{violation.Subject}";
                        }
                    }
                }

                throw;
            }
        };
    }
}
