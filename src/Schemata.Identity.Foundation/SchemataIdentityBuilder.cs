using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Core.Features;
using Schemata.Identity.Skeleton.Entities;
using Microsoft.AspNetCore.Builder;
using Schemata.Security.Skeleton;

namespace Schemata.Identity.Foundation;

public sealed class SchemataIdentityBuilder<TUser, TRole> : IResourceBuilder
    where TUser : SchemataUser, new()
    where TRole : SchemataRole
{
    public const string AuthenticationSchemeKey = "Identity:AuthenticationScheme";

    internal SchemataIdentityBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
        var registrations = Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration)) ?? new();
        Schemata.Set(nameof(ResourceSecurityRegistration), registrations);
        registrations[this] = new(
            services => new SchemataResourceBuilder(Schemata, services).WithAuthentication(),
            services => new SchemataResourceBuilder(Schemata, services).WithAuthorization(),
            scheme => Schemata.Set(AuthenticationSchemeKey, scheme));
    }

    public SchemataOptions Schemata { get; }

    public IServiceCollection Services { get; }

    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }
}
