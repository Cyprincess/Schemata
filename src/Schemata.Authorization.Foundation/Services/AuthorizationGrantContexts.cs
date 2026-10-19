using System;
using System.Text.Json;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Common;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Creates, clones, serializes, and projects trusted grant lineage.</summary>
internal static class AuthorizationGrantContexts
{
    internal static AuthorizationGrantContext Create(
        string?                subject,
        string?                scope,
        string?                sessionId,
        string?                source,
        AuthenticationContext? authentication,
        string?                profile = null
    ) {
        return new() {
            Subject        = subject,
            SubjectKind    = KindOf(subject, source),
            Profile        = ResolveProfile(profile, scope),
            NativeSessionKind = ResolveNativeSession(scope),
            Source         = source,
            Scope          = scope,
            SessionId      = sessionId,
            Authentication = Clone(authentication),
        };
    }

    internal static AuthorizationGrantContext Narrow(
        AuthorizationGrantContext source,
        string?                   scope,
        string?                   sessionId
    ) {
        return new() {
            Subject        = source.Subject,
            SubjectKind    = source.SubjectKind,
            Profile        = source.Profile == GrantProfiles.OpenIdConnect && ScopeParser.Contains(scope, Scopes.OpenId)
                ? GrantProfiles.OpenIdConnect
                : GrantProfiles.OAuth,
            Source         = source.Source,
            Scope          = scope,
            SessionId      = sessionId,
            Family         = source.Family,
            FamilyEstablished = source.FamilyEstablished,
            NativeSessionKind = source.NativeSessionKind,
            OnlineSessionAuthority = source.OnlineSessionAuthority,
            ExpiresAt      = source.ExpiresAt,
            Authentication = Clone(source.Authentication),
        };
    }

    internal static AuthorizationGrantContext? Deserialize(string? json) {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<AuthorizationGrantContext>(json, SchemataJson.Default);
    }

    internal static string? Serialize(AuthorizationGrantContext? context) {
        return context is null ? null : JsonSerializer.Serialize(context, SchemataJson.Default);
    }

    internal static string? KindOf(string? subject, string? source) {
        if (string.IsNullOrWhiteSpace(subject)) {
            return null;
        }
        return source is GrantTypes.AuthorizationCode or GrantTypes.DeviceCode
            ? GrantSubjectKinds.EndUser
            : GrantSubjectKinds.Application;
    }

    private static string? ResolveNativeSession(string? scope) {
        if (!ScopeParser.Contains(scope, Scopes.DeviceSso)) return null;
        return ScopeParser.Contains(scope, Scopes.OfflineAccess)
            ? NativeSessionKinds.Offline
            : NativeSessionKinds.Online;
    }

    private static string ResolveProfile(string? profile, string? scope) {
        return profile == GrantProfiles.OpenIdConnect && ScopeParser.Contains(scope, Scopes.OpenId)
            ? GrantProfiles.OpenIdConnect
            : GrantProfiles.OAuth;
    }

    private static AuthenticationContext? Clone(AuthenticationContext? context) {
        return context is null
            ? null
            : new(context.Acr, [.. context.Amr], context.AuthTime);
    }
}
