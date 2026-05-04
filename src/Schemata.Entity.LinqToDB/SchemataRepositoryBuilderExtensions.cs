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
    /// <param name="contextLifetime">
    ///     The service lifetime for the data connection (defaults to
    ///     <see cref="ServiceLifetime.Scoped" />).
    /// </param>
    /// <param name="optionsLifetime">
    ///     The service lifetime for the data options (defaults to
    ///     <see cref="ServiceLifetime.Scoped" />).
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseLinqToDb(
        this SchemataRepositoryBuilder                   builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime                                  contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime                                  optionsLifetime = ServiceLifetime.Scoped
    ) {
        return builder.UseLinqToDb<DataConnection, DataConnection>(configure, contextLifetime, optionsLifetime);
    }

    /// <summary>
    ///     Registers a custom <see cref="DataConnection" /> type as the LINQ to DB data provider.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DataConnection" /> type (used as both service and implementation type).</typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">A function to configure <see cref="DataOptions" />.</param>
    /// <param name="contextLifetime">
    ///     The service lifetime for the data connection (defaults to
    ///     <see cref="ServiceLifetime.Scoped" />).
    /// </param>
    /// <param name="optionsLifetime">
    ///     The service lifetime for the data options (defaults to
    ///     <see cref="ServiceLifetime.Scoped" />).
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public static SchemataRepositoryBuilder UseLinqToDb<TContext>(
        this SchemataRepositoryBuilder                   builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime                                  contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime                                  optionsLifetime = ServiceLifetime.Scoped
    )
        where TContext : DataConnection {
        return builder.UseLinqToDb<TContext, TContext>(configure, contextLifetime, optionsLifetime);
    }

    /// <summary>
    ///     Registers a LINQ to DB <see cref="DataConnection" /> with separate service and implementation types.
    /// </summary>
    /// <typeparam name="TContext">The service type for the data connection.</typeparam>
    /// <typeparam name="TContextImplementation">The implementation type for the data connection.</typeparam>
    /// <param name="builder">The repository builder.</param>
    /// <param name="configure">A function to configure <see cref="DataOptions" />.</param>
    /// <param name="contextLifetime">
    ///     The service lifetime for the data connection (defaults to
    ///     <see cref="ServiceLifetime.Scoped" />).
    /// </param>
    /// <param name="optionsLifetime">
    ///     The service lifetime for the data options (defaults to
    ///     <see cref="ServiceLifetime.Scoped" />).
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <typeparamref name="TContextImplementation" /> lacks a constructor
    ///     accepting <see cref="DataOptions" /> or a typed variant.
    /// </exception>
    public static SchemataRepositoryBuilder UseLinqToDb<TContext, TContextImplementation>(
        this SchemataRepositoryBuilder                   builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime                                  contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime                                  optionsLifetime = ServiceLifetime.Scoped
    )
        where TContextImplementation : TContext
        where TContext : DataConnection {
        // Register the default metadata reader that maps System.ComponentModel.DataAnnotations.Schema attributes
        // to LINQ to DB mapping attributes so that [Table], [Column], etc. are recognized.
        MappingSchema.Default.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var constructor = HasTypedContextConstructor<TContextImplementation, TContext>();

        builder.Services.TryAdd(
            new ServiceDescriptor(typeof(TContext), typeof(TContextImplementation), contextLifetime)
        );

        switch (constructor) {
            case OptionsParameterType.DataOptionsTImpl:
                builder.Services.TryAdd(
                    new ServiceDescriptor(
                        typeof(DataOptions<TContextImplementation>),
                        sp => new DataOptions<TContextImplementation>(configure(sp, new())),
                        optionsLifetime
                    )
                );
                break;
            case OptionsParameterType.DataOptionsTContext:
                builder.Services.TryAdd(
                    new ServiceDescriptor(
                        typeof(DataOptions<TContext>),
                        sp => new DataOptions<TContext>(configure(sp, new())),
                        optionsLifetime
                    )
                );
                break;
            case OptionsParameterType.DataOptions:
                builder.Services.TryAdd(
                    new ServiceDescriptor(typeof(DataOptions), sp => configure(sp, new()), optionsLifetime)
                );
                break;
        }

        return builder;
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
