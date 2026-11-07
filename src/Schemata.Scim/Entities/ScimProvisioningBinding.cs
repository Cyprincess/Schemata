using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Scim.Entities;

/// <summary>Maps a provisioning-domain identifier to a Schemata entity.</summary>
[Table("SchemataScimProvisioningBindings")]
[PrimaryKey(nameof(ResourceType), nameof(ProvisioningDomain), nameof(EntityUid))]
[Index(nameof(ResourceType), nameof(ProvisioningDomain), nameof(ExternalId), IsUnique = true)]
public sealed class ScimProvisioningBinding
{
    public string ResourceType { get; set; } = null!; // The repository materializes this key column.

    public Guid EntityUid { get; set; }

    public string ProvisioningDomain { get; set; } = null!; // The repository materializes this key column.

    public string? ExternalId { get; set; }
}
