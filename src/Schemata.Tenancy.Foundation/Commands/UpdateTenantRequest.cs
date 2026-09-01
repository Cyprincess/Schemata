using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Commands;

/// <summary>Requests persisting changes to an existing tenant.</summary>
/// <param name="Tenant">The tenant carrying updated values.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record UpdateTenantRequest<TTenant>(TTenant Tenant) : ICommand
    where TTenant : SchemataTenant;
