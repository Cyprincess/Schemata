using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Estimation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataRepositoryBuilder" /> to configure Entity Framework Core as the
///     repository provider.
/// </summary>
public static class SchemataRepositoryBuilderExtensions
{
    /// <summary>
    ///     Registers an Entity Framework Core <see cref="DbContext" /> as the repository data provider.
    /// </summary>
    /// <typeparam name="TContext">
    ///     The <see cref="DbContext" /> type to register (used as both the service and implementation type).
    /// </typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">Optional callback to configure <see cref="DbContextOptionsBuilder" /> per service provider.</param>
    /// <param name="lifetime">
    ///     The lifetime of the native context factory and its options; the default keeps the
    ///     global singleton semantics.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseEntityFrameworkCore<TContext>(
        this SchemataRepositoryBuilder                     builder,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure,
        ServiceLifetime                                    lifetime = ServiceLifetime.Singleton
    )
        where TContext : DbContext {
        var user = configure ?? ((_, _) => { });

        builder.Services.AddDbContextFactory<TContext>((sp, options) => {
            options.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
            user(sp, options);
        }, lifetime);

        return builder;
    }

    /// <summary>
    ///     Standard source registration entry for a repository-side native context factory:
    ///     registers <c>AddDbContextFactory</c> and the matching unit of work in the target
    ///     collection and records a local <see cref="RepositorySourceRegistration" /> so callers
    ///     can prove the source was configured here instead of inherited from the host container.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext" /> type.</typeparam>
    /// <param name="services">The target service collection.</param>
    /// <param name="configure">Callback to configure <see cref="DbContextOptionsBuilder" /> per service provider.</param>
    /// <param name="lifetime">
    ///     The lifetime of the factory and options; Scoped binds both to the composing tenant or
    ///     partition scope so its configuration is authoritative there.
    /// </param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddRepositorySource<TContext>(
        this IServiceCollection                           services,
        Action<IServiceProvider, DbContextOptionsBuilder> configure,
        ServiceLifetime                                   lifetime = ServiceLifetime.Scoped
    )
        where TContext : DbContext {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddDbContextFactory<TContext>((sp, options) => {
            options.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
            configure(sp, options);
        }, lifetime);

        services.AddSingleton(new RepositorySourceRegistration(typeof(TContext)));

        services.TryAddScoped<IUnitOfWork<TContext>, EfCoreUnitOfWork<TContext>>();

        return services;
    }

    /// <summary>
    ///     Registers a unit of work for the specified Entity Framework Core <see cref="DbContext" />,
    ///     enabling cross-repository transactions.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext" /> type.</typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder WithUnitOfWork<TContext>(this SchemataRepositoryBuilder builder)
        where TContext : DbContext {
        builder.Services.TryAddScoped<IUnitOfWork<TContext>, EfCoreUnitOfWork<TContext>>();

        return builder;
    }

    /// <summary>
    ///     Opts this context into database-plan estimates for supported root entity queries.
    /// </summary>
    /// <remarks>
    ///     Plan reads run through EF reader interception; SHOWPLAN mode commands use nonquery interception.
    ///     Interceptors receive the actual plan SQL and may reject it. Security-sensitive rewrites must
    ///     support that statement shape. Connection ownership and the current EF transaction are retained.
    /// </remarks>
    public static SchemataRepositoryBuilder WithCountEstimates<TContext>(
        this SchemataRepositoryBuilder builder,
        QueryEstimateProvider provider
    ) where TContext : DbContext {
        if (!Enum.IsDefined(provider)) {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        builder.Services.TryAddScoped<IEfCoreCountEstimator<TContext>>(_ => new EfCoreCountEstimator<TContext>(provider));
        return builder;
    }
}
