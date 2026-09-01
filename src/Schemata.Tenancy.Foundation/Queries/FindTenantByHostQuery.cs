using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Queries;

/// <summary>Queries a tenant through one of its associated host names.</summary>
/// <param name="Host">The host name to resolve.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record FindTenantByHostQuery<TTenant>(string Host) : IQuery<TTenant?>
    where TTenant : SchemataTenant;
