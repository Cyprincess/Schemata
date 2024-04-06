using System;
using Microsoft.EntityFrameworkCore;
using Schemata.Entity.Repository;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class RepositoryBuilderExtensions
{
    public static SchemataRepositoryBuilder UseEntityFrameworkCore<TContext>(
        this SchemataRepositoryBuilder                     builder,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure,
        ServiceLifetime                                    contextLifetime = ServiceLifetime.Transient,
        ServiceLifetime                                    optionsLifetime = ServiceLifetime.Scoped)
        where TContext : DbContext {

        builder.Services.AddDbContext<TContext, TContext>(configure, contextLifetime, optionsLifetime);

        return builder;
    }

    public static SchemataRepositoryBuilder UseEntityFrameworkCore<TContextService, TContextImplementation>(
        this SchemataRepositoryBuilder                     builder,
        Action<IServiceProvider, DbContextOptionsBuilder>? configure,
        ServiceLifetime                                    contextLifetime = ServiceLifetime.Transient,
        ServiceLifetime                                    optionsLifetime = ServiceLifetime.Scoped)
        where TContextImplementation : DbContext, TContextService {

        builder.Services.AddDbContext<TContextService, TContextImplementation>(configure, contextLifetime, optionsLifetime);

        return builder;
    }
}
