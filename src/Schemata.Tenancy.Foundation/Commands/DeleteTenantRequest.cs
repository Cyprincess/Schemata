using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Commands;

/// <summary>Requests deletion of a tenant together with its host associations.</summary>
/// <param name="Tenant">The tenant to remove.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record DeleteTenantRequest<TTenant>(TTenant Tenant) : ICommand
    where TTenant : SchemataTenant;
