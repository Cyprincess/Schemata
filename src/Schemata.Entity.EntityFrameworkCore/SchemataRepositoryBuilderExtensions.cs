using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseEntityFrameworkCore<TContext>(
        this SchemataRepositoryBuilder                     builder,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure
    )
        where TContext : DbContext {
        var user = configure ?? ((_, _) => { });

        builder.Services.AddDbContextFactory<TContext>((sp, options) => {
            options.ReplaceService<IModelCustomizer, SchemataModelCustomizer>();
            user(sp, options);
        });

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
