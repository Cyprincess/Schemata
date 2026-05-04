using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;

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
    /// <param name="contextLifetime">The service lifetime for the context (defaults to <see cref="ServiceLifetime.Scoped" />).</param>
    /// <param name="optionsLifetime">The service lifetime for the options (defaults to <see cref="ServiceLifetime.Scoped" />).</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseEntityFrameworkCore<TContext>(
        this SchemataRepositoryBuilder                     builder,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure,
        ServiceLifetime                                    contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime                                    optionsLifetime = ServiceLifetime.Scoped
    )
        where TContext : DbContext {
        builder.Services.AddDbContext<TContext, TContext>(configure, contextLifetime, optionsLifetime);

        return builder;
    }

    /// <summary>
    ///     Registers an Entity Framework Core <see cref="DbContext" /> as the repository data provider with separate
    ///     service and implementation types.
    /// </summary>
    /// <typeparam name="TContextService">The service type for the context.</typeparam>
    /// <typeparam name="TContextImplementation">The implementation type for the context.</typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">Optional callback to configure <see cref="DbContextOptionsBuilder" /> per service provider.</param>
    /// <param name="contextLifetime">The service lifetime for the context (defaults to <see cref="ServiceLifetime.Scoped" />).</param>
    /// <param name="optionsLifetime">The service lifetime for the options (defaults to <see cref="ServiceLifetime.Scoped" />).</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseEntityFrameworkCore<TContextService, TContextImplementation>(
        this SchemataRepositoryBuilder                     builder,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure,
        ServiceLifetime                                    contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime                                    optionsLifetime = ServiceLifetime.Scoped
    )
        where TContextImplementation : DbContext, TContextService {
        builder.Services.AddDbContext<TContextService, TContextImplementation>(
            configure,
            contextLifetime,
            optionsLifetime
        );

        return builder;
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
}
