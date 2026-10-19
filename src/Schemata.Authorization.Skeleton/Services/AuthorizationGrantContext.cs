using System;
using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Trusted grant lineage from the original authentication event. The authorization server
///     creates it after session resolution and carries it through code, refresh, and validated
///     token-exchange continuations. Clients never supply this context.
/// </summary>
public sealed class AuthorizationGrantContext
{
    /// <summary>Canonical subject that the original grant authenticated.</summary>
    public string? Subject { get; set; }

    /// <summary>Trusted subject category: <see cref="AuthorizationConstants.GrantSubjectKinds.EndUser" /> or <see cref="AuthorizationConstants.GrantSubjectKinds.Application" />.</summary>
    public string? SubjectKind { get; set; }

    /// <summary>Effective authorization profile: <see cref="AuthorizationConstants.GrantProfiles.OAuth" /> or <see cref="AuthorizationConstants.GrantProfiles.OpenIdConnect" />.</summary>
    public string Profile { get; set; } = AuthorizationConstants.GrantProfiles.OAuth;

    /// <summary>Trusted source that established or continued the context (normally the server-selected grant type).</summary>
    public string? Source { get; set; }

    /// <summary>Effective scope of this continuation; narrowing updates descendants only.</summary>
    public string? Scope { get; set; }

    /// <summary>Optional OP session association.</summary>
    public string? SessionId { get; set; }

    /// <summary>Refresh-family semantic identifier inherited by every descendant.</summary>
    public string? Family { get; set; }

    /// <summary>Whether the family marker already exists in persistent storage.</summary>
    public bool FamilyEstablished { get; set; }

    /// <summary>Native SSO session authority: <see cref="AuthorizationConstants.NativeSessionKinds.Online" /> or <see cref="AuthorizationConstants.NativeSessionKinds.Offline" />.</summary>
    public string? NativeSessionKind { get; set; }

    /// <summary>
    ///     Opaque online-authority generation the grant was established under; valid only while the
    ///     (subject, sid) OP-session slot is live and carries this exact value.
    /// </summary>
    public string? OnlineSessionAuthority { get; set; }

    /// <summary>Absolute UTC deadline inherited by refresh descendants.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Original authentication evidence; absent members remain absent.</summary>
    public AuthenticationContext? Authentication { get; set; }
}