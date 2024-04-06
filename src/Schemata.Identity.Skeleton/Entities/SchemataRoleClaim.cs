using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Entities;

namespace Schemata.Identity.Skeleton.Entities;

[Table("RoleClaims")]
public class SchemataRoleClaim : IdentityRoleClaim<long>, IIdentifier, ITimestamp
{
    public new long Id { get; set; }

    public DateTime? CreationDate { get; set; }

    public DateTime? ModificationDate { get; set; }
}
