using System;
using System.Linq;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
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
        AuthorizationGrantContext? grant,
        DateTime now
    ) {
        return !string.IsNullOrWhiteSpace(token.ReferenceId)
            && token.Type == TokenTypes.DeviceSecret
            && token.Status == TokenStatuses.Valid
            && (token.ExpireTime is null || token.ExpireTime > now)
            && string.Equals(token.Application, SecurityParents.Application(application), StringComparison.Ordinal)
            && string.Equals(token.SessionId, sessionId, StringComparison.Ordinal)
            && EventMatches(token, grant)
            && (string.IsNullOrWhiteSpace(deviceId)
                || string.Equals(token.DeviceId, deviceId, StringComparison.Ordinal));
    }

    private static bool EventMatches(SchemataToken token, AuthorizationGrantContext? expected) {
        var stored = AuthorizationGrantContexts.Deserialize(token.GrantContext);
        return expected is not null
            && stored is not null
            && string.Equals(stored.Subject, expected.Subject, StringComparison.Ordinal)
            && string.Equals(stored.SubjectKind, expected.SubjectKind, StringComparison.Ordinal)
            && string.Equals(stored.Profile, expected.Profile, StringComparison.Ordinal)
            && string.Equals(stored.Source, expected.Source, StringComparison.Ordinal)
            && string.Equals(stored.SessionId, expected.SessionId, StringComparison.Ordinal)
            && string.Equals(token.Family, expected.Family, StringComparison.Ordinal)
            && ScopeParser.Parse(stored.Scope).SetEquals(ScopeParser.Parse(expected.Scope))
            && AuthenticationMatches(stored.Authentication, expected.Authentication)
            && OnlineAuthorityMatches(stored, expected);
    }

    // An online secret stays bound to the exact online-authority generation it was issued under;
    // expiry or logout followed by reauthorization yields a fresh generation and a fresh secret.
    private static bool OnlineAuthorityMatches(AuthorizationGrantContext stored, AuthorizationGrantContext expected) {
        if (!string.Equals(stored.NativeSessionKind, expected.NativeSessionKind, StringComparison.Ordinal)) {
            return false;
        }

        if (expected.NativeSessionKind != NativeSessionKinds.Online) {
            return true;
        }

        return !string.IsNullOrWhiteSpace(expected.OnlineSessionAuthority)
            && string.Equals(stored.OnlineSessionAuthority, expected.OnlineSessionAuthority, StringComparison.Ordinal);
    }

    private static bool AuthenticationMatches(AuthenticationContext? left, AuthenticationContext? right) {
        if (left is null || right is null) {
            return left is null && right is null;
        }

        return string.Equals(left.Acr, right.Acr, StringComparison.Ordinal)
            && left.AuthTime == right.AuthTime
            && left.Amr.SequenceEqual(right.Amr, StringComparer.Ordinal);
    }

}