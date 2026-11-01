using System;

namespace Schemata.Abstractions.Entities;

public interface ITenant
{
    /// <summary>A null value identifies a host-owned row.</summary>
    Guid? TenantUid { get; set; }
}
