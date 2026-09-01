using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Commands;

/// <summary>Requests creation of a tenant.</summary>
/// <param name="Tenant">The tenant to persist.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record CreateTenantRequest<TTenant>(TTenant Tenant) : ICommand
    where TTenant : SchemataTenant;
