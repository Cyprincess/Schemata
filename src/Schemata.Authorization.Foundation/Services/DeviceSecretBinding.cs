using System;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

internal static class DeviceSecretBinding
{
    public static bool IsUsable(
        SchemataToken token,
        SchemataApplication application,
        string? sessionId,
        string? deviceId,
        DateTime now
    ) {
        return !string.IsNullOrWhiteSpace(token.ReferenceId)
            && token.Type == TokenTypes.DeviceSecret
            && token.Status == TokenStatuses.Valid
            && (token.ExpireTime is null || token.ExpireTime > now)
            && string.Equals(token.Application, SecurityParents.Application(application), StringComparison.Ordinal)
            && string.Equals(token.SessionId, sessionId, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(deviceId)
                || string.Equals(token.DeviceId, deviceId, StringComparison.Ordinal));
    }

    public static string? ClientId(SchemataToken token) {
        const string prefix = "applications/";
        return token.Application?.StartsWith(prefix, StringComparison.Ordinal) == true
            ? token.Application[prefix.Length..]
            : null;
    }
}

internal sealed record DeviceSecretIssuance(
    string DeviceSecret,
    string? DeviceId,
    string? SourceClientId,
    string? SessionId
);
