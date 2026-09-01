using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;

namespace Schemata.Core.Building;

/// <summary>
///     Callbacks the resource package attaches to a <see cref="ResourceRegistry" />: the per-resource
///     handler and advisor registration, and the security-stage envelope closing. The registry
///     replays them over everything registered or activated before the attach, so builders,
///     security activation, and resource registration produce the same outcome in any order.
/// </summary>
/// <param name="RegisterResource">Registers the handlers and pipeline advisors for one resource.</param>
/// <param name="RegisterAuthentication">Closes the authentication advisors over one resource.</param>
/// <param name="RegisterAuthorization">Closes the authorization advisors over one resource.</param>
/// <param name="RegisterAuthorizationAdvisors">Registers the cross-resource authorization advisors.</param>
public sealed record ResourcePipelineWiring(
    Action<IServiceCollection, ResourceAttribute, IReadOnlyList<ResourceMethodAttribute>> RegisterResource,
    Action<IServiceCollection, ResourceAttribute, IReadOnlyList<ResourceMethodAttribute>> RegisterAuthentication,
    Action<IServiceCollection, ResourceAttribute, IReadOnlyList<ResourceMethodAttribute>> RegisterAuthorization,
    Action<IServiceCollection>                                                                RegisterAuthorizationAdvisors
);
