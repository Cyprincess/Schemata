using System.Linq;
using Microsoft.AspNetCore.RateLimiting;
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
            var identity = TenantContext.Current;
            var supplied = context.Lease.TryGetMetadata(QuotaMetadata.Subject, out var subject)
                        && !string.IsNullOrWhiteSpace(subject);
            try {
                await rejected(context, ct);
            } catch (QuotaExceededException error) when (!supplied && identity.Uid.HasValue) {
                if (error.Details is { } details) {
                    foreach (var detail in details.OfType<QuotaFailureDetail>()) {
                        if (detail.Violations is not { } violations) {
                            continue;
                        }

                        foreach (var violation in violations) {
                            violation.Subject = $"tenants/{identity.Uid.Value:D}/{violation.Subject}";
                        }
                    }
                }

                throw;
            }
        };
    }
}
