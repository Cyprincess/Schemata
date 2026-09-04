using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Security.Skeleton.Entities;

/// <summary>
///     A stored security material row: password hash, plaintext secret, asymmetric key, JOSE material,
///     or certificate, attached to any host resource.
/// </summary>
[Table("SchemataSecurities")]
[CanonicalName("securities/{security}")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Parent), nameof(Kind), nameof(Usage))]
public class SchemataSecurity : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>Polymorphic parent reference: canonical name of any host resource (applications/{x}, users/{y}, issuer URI).</summary>
    [ResourceReference]
    public virtual string? Parent { get; set; }

    /// <summary>Material category. See <see cref="SecurityConstants.Kinds" />.</summary>
    public virtual string? Kind { get; set; }

    /// <summary>Hash format (pbkdf2/bcrypt/argon2id) or key algorithm. Open string, not a closed enum.
    /// Private-key rows require a PEM-importable key algorithm: rsa / p-256 / p-384 / p-521.</summary>
    public virtual string? Algorithm { get; set; }

    /// <summary>Purpose within the parent. See <see cref="SecurityConstants.Usages" />. Single word, parent-scoped.</summary>
    public virtual string? Usage { get; set; }

    /// <summary>Key identifier (JWK kid); rotation and assertion kid matching.</summary>
    public virtual string? Kid { get; set; }

    /// <summary>Material: password → hash string; secret/private-key → plaintext material; jwk/jwks → JSON; *-uri → URI; public-key → PEM.</summary>
    public virtual string? Value { get; set; }

    /// <summary>Lifecycle: valid / retired (still verifiable) / revoked (never). See <see cref="SecurityConstants.Statuses" />.</summary>
    public virtual string? Status { get; set; }

    /// <inheritdoc cref="IIdentifier.Uid" />
    public virtual Guid Uid { get; set; }

    /// <inheritdoc cref="ICanonicalName.Name" />
    public virtual string? Name { get; set; }

    /// <inheritdoc cref="ICanonicalName.CanonicalName" />
    public virtual string? CanonicalName { get; set; }

    /// <inheritdoc cref="IConcurrency.Timestamp" />
    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    /// <inheritdoc cref="ITimestamp.CreateTime" />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc cref="ITimestamp.UpdateTime" />
    public virtual DateTime? UpdateTime { get; set; }
}
