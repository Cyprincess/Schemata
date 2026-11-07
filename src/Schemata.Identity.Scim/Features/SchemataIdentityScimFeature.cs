using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Scim.Handlers;
using Schemata.Identity.Scim.Services;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Scim.Services;
using Schemata.Transport.Http.Features;

namespace Schemata.Identity.Scim.Features;

/// <summary>Registers the selected Identity entity types with the shared SCIM protocol.</summary>
/// <typeparam name="TUser">The Identity user entity type.</typeparam>
/// <typeparam name="TRole">The Identity role entity type.</typeparam>
[DependsOn<SchemataIdentityFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataIdentityScimFeature<TUser, TRole> : FeatureBase
    where TUser : SchemataUser, new()
    where TRole : SchemataRole, new()
{
    public const int DefaultPriority = SchemataIdentityFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataScim();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScimResourceHandler, ScimUserHandler<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScimResourceHandler, ScimGroupHandler<TUser, TRole>>());
        services.TryAddScoped<IScimUserGroupReader<TUser>, ScimGroupHandler<TUser, TRole>>();
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) => app.UseSchemataScimErrors();
}
