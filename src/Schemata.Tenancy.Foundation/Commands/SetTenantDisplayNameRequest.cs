using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Commands;

/// <summary>Requests setting a tenant's invariant display name.</summary>
/// <param name="Tenant">The tenant whose display name changes.</param>
/// <param name="DisplayName">The display name to assign, or <see langword="null" /> to clear it.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record SetTenantDisplayNameRequest<TTenant>(TTenant Tenant, string? DisplayName) : ICommand
    where TTenant : SchemataTenant;
