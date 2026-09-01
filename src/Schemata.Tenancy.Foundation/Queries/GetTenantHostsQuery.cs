using System.Collections.Immutable;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Queries;

/// <summary>Queries the host names associated with a tenant.</summary>
/// <param name="Tenant">The tenant whose host names are requested.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record GetTenantHostsQuery<TTenant>(TTenant Tenant) : IQuery<ImmutableArray<string>>
    where TTenant : SchemataTenant;
