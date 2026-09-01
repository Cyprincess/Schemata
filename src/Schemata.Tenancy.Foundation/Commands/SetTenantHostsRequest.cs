using System.Collections.Immutable;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Commands;

/// <summary>Requests replacing a tenant's host associations.</summary>
/// <param name="Tenant">The tenant whose host associations are replaced.</param>
/// <param name="Hosts">The requested host names; blank entries are dropped.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record SetTenantHostsRequest<TTenant>(TTenant Tenant, ImmutableArray<string> Hosts) : ICommand
    where TTenant : SchemataTenant;
