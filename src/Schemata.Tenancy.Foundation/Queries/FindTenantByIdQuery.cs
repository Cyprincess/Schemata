using System;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Queries;

/// <summary>Queries a tenant by its unique identifier.</summary>
/// <param name="TenantId">The tenant's unique identifier.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record FindTenantByIdQuery<TTenant>(Guid TenantId) : IQuery<TTenant?>
    where TTenant : SchemataTenant;
