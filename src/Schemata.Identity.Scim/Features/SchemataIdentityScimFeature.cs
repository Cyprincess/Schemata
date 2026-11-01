using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Scim.Controllers;
using Schemata.Identity.Scim.Features;
using Schemata.Identity.Scim.Services;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Transport.Http.Features;

namespace Schemata.Identity.Scim.Features
{
    /// <summary>
    ///     Configures the Identity SCIM adapter.
    /// </summary>
    public sealed class SchemataIdentityScimOptions
    {
        /// <summary>
        ///     The authorization policy evaluated for SCIM requests. The host's default policy
        ///     applies when unset.
        /// </summary>
        public string? AuthorizationPolicy { get; set; }

        /// <summary>The number of users returned when a request omits <c>count</c>.</summary>
        public int DefaultPageSize { get; set; } = 100;

        /// <summary>The largest <c>count</c> a request may select.</summary>
        public int MaxPageSize { get; set; } = 200;
    }

    /// <summary>
    ///     Exposes the Identity user entity as the SCIM User resource and installs the shared
    ///     protocol error handling for the SCIM surface.
    /// </summary>
    /// <typeparam name="TUser">The identity user entity type.</typeparam>
    [DependsOn<SchemataIdentityFeature>]
    [DependsOn<SchemataTransportHttpFeature>]
    public sealed class SchemataIdentityScimFeature<TUser> : FeatureBase
        where TUser : SchemataUser, new()
    {
        /// <summary>The default feature priority for the Identity SCIM adapter.</summary>
        public const int DefaultPriority = SchemataIdentityFeature.DefaultPriority + 100_000;

        public override int Priority => DefaultPriority;

        public override void ConfigureServices(
            IServiceCollection  services,
            SchemataOptions     schemata,
            Configurators       configurators,
            IConfiguration      configuration,
            IWebHostEnvironment environment
        ) {
            services.Configure(configurators.PopOrDefault<SchemataIdentityScimOptions>());
            var groups = HasGroupServices(services);
            services.Configure<MvcOptions>(options => options.Conventions.Add(new ScimGroupControllerConvention(groups)));
            services.AddSchemataScimResourceType(ScimUserAdapter<TUser>.ResourceType, ScimUserAdapter<TUser>.Schema);
            services.AddSchemataApplicationPart<ScimUsersController>();
            services.TryAddScoped<IScimUserService, ScimUserAdapter<TUser>>();

            if (!groups) {
                return;
            }

            services.AddSchemataScimResourceType(ScimGroupAdapter<TUser>.ResourceType, ScimGroupAdapter<TUser>.Schema);
            services.AddSchemataApplicationPart<ScimGroupsController>();
            services.TryAddScoped<ScimGroupAdapter<TUser>>();
            services.TryAddScoped<IScimGroupService>(provider => provider.GetRequiredService<ScimGroupAdapter<TUser>>());
            services.TryAddScoped<IScimUserGroupReader<TUser>>(provider => provider.GetRequiredService<ScimGroupAdapter<TUser>>());
        }

        private static bool HasGroupServices(IServiceCollection services) {
            // Groups write default SchemataRole rows. A host that selected a custom role type has no
            // default role services, so the endpoint and discovery resource stay unregistered rather
            // than activating a controller whose dependencies cannot resolve.
            return services.Any(descriptor => descriptor.ServiceType == typeof(RoleManager<SchemataRole>))
                && services.Any(descriptor => descriptor.ServiceType == typeof(IRepository<SchemataRole>))
                && services.Any(descriptor => descriptor.ServiceType == typeof(IRepository<SchemataRoleClaim>))
                && services.Any(descriptor => descriptor.ServiceType == typeof(IRepository<SchemataUserRole>))
                && services.Any(descriptor => descriptor.ServiceType == typeof(IResourceMutation<SchemataRole>));
        }

        public override void ConfigureApplication(
            IApplicationBuilder app,
            IConfiguration      configuration,
            IWebHostEnvironment environment
        ) => app.UseSchemataScimErrors();
    }

    /// <summary>
    ///     Removes the Groups controller from MVC when the default role services are absent. The
    ///     application part exposes the whole adapter assembly, so an explicit registration alone
    ///     cannot keep the endpoint off a custom-role host.
    /// </summary>
    /// <param name="enabled">Whether the default role services are available.</param>
    internal sealed class ScimGroupControllerConvention(bool enabled) : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application) {
            if (enabled) {
                return;
            }

            var controller = application.Controllers.FirstOrDefault(
                model => model.ControllerType == typeof(ScimGroupsController));
            if (controller is not null) {
                application.Controllers.Remove(controller);
            }
        }
    }
}

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    ///     Configures the Identity SCIM feature for the host-selected user type.
    /// </summary>
    public static class SchemataIdentityScimBuilderExtensions
    {
        /// <summary>Adds the SCIM User adapter for <typeparamref name="TUser" />.</summary>
        /// <typeparam name="TUser">The identity user entity type.</typeparam>
        /// <param name="builder">The Schemata builder.</param>
        /// <returns>The Schemata builder for chaining.</returns>
        public static SchemataBuilder UseIdentityScim<TUser>(this SchemataBuilder builder)
            where TUser : SchemataUser, new() {
            builder.AddFeature<SchemataIdentityScimFeature<TUser>>();

            return builder;
        }
    }
}
