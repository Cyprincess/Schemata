using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Tenancy.Actor;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;

namespace Microsoft.AspNetCore.Builder;

public static class TenantActorExtensions
{
    /// <summary>
    ///     Routes actor operations per tenant for the default persisted actor-state entity
    ///     <see cref="SchemataActor" />.
    /// </summary>
    /// <typeparam name="TTenant">The tenant entity type.</typeparam>
    /// <param name="builder">The tenancy builder.</param>
    /// <returns>The tenancy builder for chaining.</returns>
    public static SchemataTenancyBuilder<TTenant> UseActor<TTenant>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant {
        return builder.UseActor<TTenant, SchemataActor>();
    }

    /// <summary>
    ///     Routes actor operations per tenant for the host-defined persisted actor-state entity
    ///     type <typeparamref name="TActor" />.
    /// </summary>
    /// <typeparam name="TTenant">The tenant entity type.</typeparam>
    /// <typeparam name="TActor">The persisted actor-state entity type registered by the host.</typeparam>
    /// <param name="builder">The tenancy builder.</param>
    /// <returns>The tenancy builder for chaining.</returns>
    public static SchemataTenancyBuilder<TTenant> UseActor<TTenant, TActor>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant
        where TActor : SchemataActor, new() {
        builder.UseMessaging();
        builder.Services.AddSchemataActorSystem<TenantActorSystemFactory<TActor>>();
        return builder;
    }

    /// <summary>
    ///     Registers the state store for the default persisted actor-state entity
    ///     <see cref="SchemataActor" /> in every tenant container.
    /// </summary>
    /// <typeparam name="TTenant">The tenant entity type.</typeparam>
    /// <param name="builder">The tenancy builder.</param>
    /// <returns>The tenancy builder for chaining.</returns>
    public static SchemataTenancyBuilder<TTenant> UseActorPersistence<TTenant>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant {
        return builder.UseActorPersistence<TTenant, SchemataActor>();
    }

    /// <summary>
    ///     Registers the state store for <typeparamref name="TActor" /> in every tenant container.
    /// </summary>
    /// <typeparam name="TTenant">The tenant entity type.</typeparam>
    /// <typeparam name="TActor">The persisted actor-state entity type registered by the host.</typeparam>
    /// <param name="builder">The tenancy builder.</param>
    /// <returns>The tenancy builder for chaining.</returns>
    public static SchemataTenancyBuilder<TTenant> UseActorPersistence<TTenant, TActor>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant
        where TActor : SchemataActor, new() {
        builder.Services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, services, _) => {
            services.TryAddScoped<ActorStateStore<TActor>>();
        }));
        return builder;
    }
}
