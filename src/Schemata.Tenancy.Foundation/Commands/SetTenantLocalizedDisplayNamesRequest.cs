using System.Collections.Generic;
using Schemata.Messaging.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Commands;

/// <summary>Requests setting a tenant's culture-localized display names.</summary>
/// <param name="Tenant">The tenant whose localized display names change.</param>
/// <param name="DisplayNames">The localized names keyed by culture.</param>
/// <typeparam name="TTenant">The tenant entity type.</typeparam>
public sealed record SetTenantLocalizedDisplayNamesRequest<TTenant>(
    TTenant                     Tenant,
    Dictionary<string, string?> DisplayNames
) : ICommand
    where TTenant : SchemataTenant;
