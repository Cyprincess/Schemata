using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Persistent (application, canonical-subject) → pairwise-subject mapping per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#SubjectIDTypes">
///         OpenID Connect Core 1.0 §8: Subject Identifier Types
///     </seealso>
///     . Guarantees per-app per-subject pairwise stability and supports reverse lookup
///     when a pairwise client passes its rotated <c>sub</c> back to a user-facing
///     resource endpoint.
/// </summary>
/// <remarks>
///     The forward direction is deterministic (SHA-256 over sector + canonical + salt)
///     so the entity could be recomputed on the fly, but persistence is required to:
///     <list type="bullet">
///         <item>survive process restarts and node failover;</item>
///         <item>preserve issued pairwise identifiers if the salt or algorithm rotates;</item>
///         <item>enable efficient <c>(application, pairwise) → canonical</c> reverse lookup
///               (the hash itself is one-way).</item>
///     </list>
/// </remarks>
[DisplayName("SubjectMapping")]
[Table("SchemataSubjectMappings")]
[CanonicalName("subjectMappings/{subjectMapping}")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Application), nameof(CanonicalSubject), IsUnique = true)]
[Index(nameof(Application), nameof(PairwiseSubject),  IsUnique = true)]
public class SchemataSubjectMapping : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>Canonical name of the OAuth application this mapping is scoped to.</summary>
    [ResourceReference(typeof(SchemataApplication))]
    public virtual string? Application { get; set; }

    /// <summary>
    ///     Canonical name of the subject (typically <c>users/{uid}</c>) the pairwise
    ///     identifier projects from. Marked polymorphic because a server may expose
    ///     non-<c>users</c> subjects (service accounts, agents, etc.) to OAuth clients.
    /// </summary>
    [ResourceReference]
    public virtual string? CanonicalSubject { get; set; }

    /// <summary>The pairwise <c>sub</c> value emitted to the client. Base64URL-encoded SHA-256 hash.</summary>
    public virtual string? PairwiseSubject { get; set; }

    /// <summary>
    ///     Sector host the pairwise hash was computed against (from
    ///     <c>SchemataApplication.SectorIdentifierUri</c> or the first redirect URI host).
    ///     Stored for audit and to detect stale rows when an application's sector changes.
    /// </summary>
    public virtual string? SectorHost { get; set; }

    #region ICanonicalName Members

    public virtual string? Name          { get; set; }
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
