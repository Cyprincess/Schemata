// Portions adapted from linq2db
// https://github.com/linq2db/linq2db/blob/4120b637104810a1b0c4d338794257579fa1604d/Source/LinqToDB.AspNet/ServiceConfigurationExtensions.cs
// Licensed under the MIT License.
// Copyright (c) 2024 Igor Tkachev, Ilya Chudin, Svyatoslav Danyliv, Dmitry Lukashenko

using System;
using System.Linq;
using System.Reflection;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for <see cref="SchemataRepositoryBuilder" /> to configure LINQ to DB as the repository provider.
/// </summary>
public static class SchemataRepositoryBuilderExtensions
{
    /// <summary>
    ///     Registers the default <see cref="DataConnection" /> as the LINQ to DB data provider.
    /// </summary>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">A function to configure <see cref="DataOptions" />.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseLinqToDb(
        this SchemataRepositoryBuilder                   builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure
    ) {
        return builder.UseLinqToDb<DataConnection, DataConnection>(configure);
    }

    /// <summary>
    ///     Registers a custom <see cref="DataConnection" /> type as the LINQ to DB data provider.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DataConnection" /> type (used as both service and implementation type).</typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">A function to configure <see cref="DataOptions" />.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseLinqToDb<TContext>(
        this SchemataRepositoryBuilder                   builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure
    )
        where TContext : DataConnection {
        return builder.UseLinqToDb<TContext, TContext>(configure);
    }

    /// <summary>
    ///     Registers a LINQ to DB <see cref="DataConnection" /> with separate service and implementation types.
    /// </summary>
    /// <typeparam name="TContext">The service type for the data connection.</typeparam>
    /// <typeparam name="TContextImplementation">The implementation type for the data connection.</typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">A function to configure <see cref="DataOptions" />.</param>
    /// <param name="lifetime">
    ///     The lifetime of the native connection factory delegate; the default keeps the
    ///     per-scope global semantics.
    /// </param>
    /// <param name="optionsLifetime">The lifetime of the composed <see cref="DataOptions" />.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <typeparamref name="TContextImplementation" /> lacks a constructor
    ///     accepting <see cref="DataOptions" /> or a typed variant.
    /// </exception>
    public static SchemataRepositoryBuilder UseLinqToDb<TContext, TContextImplementation>(
        this SchemataRepositoryBuilder                   builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime                                  lifetime        = ServiceLifetime.Scoped,
        ServiceLifetime                                  optionsLifetime = ServiceLifetime.Singleton
    )
        where TContextImplementation : TContext
        where TContext : DataConnection {
        AddConnectionServices<TContext, TContextImplementation>(builder.Services, configure, lifetime, optionsLifetime);

        return builder;
    }

    /// <summary>
    ///     Standard source registration entry for a repository-side native connection factory:
    ///     registers the scoped connection delegate, the <see cref="DataOptions" /> in the same
    ///     scope (host options are never cached), and the matching unit of work in the target
    ///     collection, and records a local <see cref="RepositorySourceRegistration" /> so callers
    ///     can prove the source was configured here instead of inherited from the host container.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
    /// <param name="services">The target service collection.</param>
    /// <param name="configure">A function to configure <see cref="DataOptions" />.</param>
    /// <param name="lifetime">
    ///     The lifetime of the factory delegate and options; Scoped binds both to the composing
    ///     tenant or partition scope so its configuration is authoritative there.
    /// </param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddRepositorySource<TContext>(
        this IServiceCollection                          services,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime                                  lifetime = ServiceLifetime.Scoped
    )
        where TContext : DataConnection {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddConnectionServices<TContext, TContext>(services, configure, lifetime, lifetime);

        services.AddSingleton(new RepositorySourceRegistration(typeof(TContext)));

        services.TryAddScoped<IUnitOfWork<TContext>, LinqToDbUnitOfWork<TContext>>();

        return services;
    }

    private static void AddConnectionServices<TContext, TContextImplementation>(
        IServiceCollection                               services,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime                                  lifetime,
        ServiceLifetime                                  optionsLifetime
    )
        where TContextImplementation : TContext
        where TContext : DataConnection {
        var constructor = HasTypedContextConstructor<TContextImplementation, TContext>();

        // A singleton delegate captures the root provider, so scoped constructor
        // dependencies of TContextImplementation would resolve from the root container.
        // Scoped registration binds the delegate to the calling scope; every invocation
        // still creates a fresh connection.
        services.TryAdd(new ServiceDescriptor(
            typeof(Func<TContext>),
            sp => (Func<TContext>)(() => ActivatorUtilities.CreateInstance<TContextImplementation>(sp)),
            lifetime));

        switch (constructor) {
            case OptionsParameterType.DataOptionsTImpl:
                services.TryAdd(new ServiceDescriptor(
                    typeof(DataOptions<TContextImplementation>),
                    sp => new DataOptions<TContextImplementation>(ComposeOptions(configure, sp)),
                    optionsLifetime));
                break;
            case OptionsParameterType.DataOptionsTContext:
                services.TryAdd(new ServiceDescriptor(
                    typeof(DataOptions<TContext>),
                    sp => new DataOptions<TContext>(ComposeOptions(configure, sp)),
                    optionsLifetime));
                break;
            case OptionsParameterType.DataOptions:
                services.TryAdd(new ServiceDescriptor(
                    typeof(DataOptions),
                    sp => ComposeOptions(configure, sp),
                    optionsLifetime));
                break;
        }
    }

    private static DataOptions ComposeOptions(Func<IServiceProvider, DataOptions, DataOptions> configure, IServiceProvider services) {
        var options = configure(services, new());
        var metadata = new MappingSchema();
        metadata.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        var mapping = options.ConnectionOptions.MappingSchema is { } application
            ? new MappingSchema(application, metadata)
            : metadata;
        return options.UseMappingSchema(mapping);
    }

    /// <summary>
    ///     Registers a unit of work for the specified LINQ to DB <see cref="DataConnection" />,
    ///     enabling cross-repository transactions.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder WithUnitOfWork<TContext>(this SchemataRepositoryBuilder builder)
        where TContext : DataConnection {
        builder.Services.TryAddScoped<IUnitOfWork<TContext>, LinqToDbUnitOfWork<TContext>>();

        return builder;
    }

    private static OptionsParameterType HasTypedContextConstructor<TContextImplementation, TContext>()
        where TContextImplementation : IDataContext
        where TContext : IDataContext {
        var constructors = typeof(TContextImplementation).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (constructors.Any(c => c.GetParameters()
                                   .Any(p => p.ParameterType == typeof(DataOptions<TContextImplementation>))
            )) {
            return OptionsParameterType.DataOptionsTImpl;
        }

        if (constructors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(DataOptions<TContext>)))) {
            return OptionsParameterType.DataOptionsTContext;
        }

        if (constructors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(DataOptions)))) {
            return OptionsParameterType.DataOptions;
        }

        throw new ArgumentException(
            $"Missing constructor accepting '{nameof(DataOptions)}' on type {typeof(TContextImplementation).Name}."
        );
    }

    #region Nested type: OptionsParameterType

    private enum OptionsParameterType
    {
        DataOptionsTImpl, DataOptionsTContext, DataOptions,
    }

    #endregion
}
