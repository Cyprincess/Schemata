using System;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Foundation.Services;

/// <summary>Applies the configured retention window to retired key material.</summary>
public static class SecurityKeyLifecycle
{
    /// <summary>Returns whether a retired row remains eligible for verification, decryption, and publication.</summary>
    /// <param name="security">Security row to inspect.</param>
    /// <param name="now">Current UTC instant.</param>
    /// <param name="retention">Retention interval measured from <see cref="SchemataSecurity.RetirementTime" />.</param>
    /// <remarks>A row without a retirement timestamp has no retention boundary.</remarks>
    public static bool IsRetained(SchemataSecurity security, DateTimeOffset now, TimeSpan retention) {
        if (security.Status != SecurityConstants.Statuses.Retired) {
            return false;
        }

        return security.RetirementTime is not { } retired || now.UtcDateTime < retired.Add(retention);
    }
}
