// Some code is borrowed from linq2db
// https://github.com/linq2db/linq2db/blob/4120b637104810a1b0c4d338794257579fa1604d/Source/LinqToDB.AspNet/ServiceConfigurationExtensions.cs
// The borrowed code is licensed under the MIT License:
//
// Copyright (c) 2024 Igor Tkachev, Ilya Chudin, Svyatoslav Danyliv, Dmitry Lukashenko
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

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

public static class SchemataRepositoryBuilderExtensions
{
    public static SchemataRepositoryBuilder UseLinqToDb(
        this SchemataRepositoryBuilder                   builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime                                  contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime                                  optionsLifetime = ServiceLifetime.Scoped) {
        return UseLinqToDb<DataConnection, DataConnection>(builder, configure, contextLifetime, optionsLifetime);
    }

    public static SchemataRepositoryBuilder UseLinqToDb<TContext>(
        this SchemataRepositoryBuilder builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped) where TContext : DataConnection {
        return UseLinqToDb<TContext, TContext>(builder, configure, contextLifetime, optionsLifetime);
    }

    public static SchemataRepositoryBuilder UseLinqToDb<TContext, TContextImplementation>(
        this SchemataRepositoryBuilder builder,
        Func<IServiceProvider, DataOptions, DataOptions> configure,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped) where TContextImplementation : TContext
                                                                  where TContext : DataConnection {
        // Register default metadata reader for System.ComponentModel.DataAnnotations.Schema attributes
        // This is required for LINQ to DB to work with entities that have TableAttribute, ColumnAttribute, etc.
        MappingSchema.Default.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());

        var constructor = HasTypedContextConstructor<TContextImplementation, TContext>();

        builder.Services.TryAdd(new ServiceDescriptor(typeof(TContext), typeof(TContextImplementation), contextLifetime));

        switch (constructor) {
            case OptionsParameterType.DataOptionsTImpl:
                builder.Services.TryAdd(new ServiceDescriptor(typeof(DataOptions<TContextImplementation>), sp => new DataOptions<TContextImplementation>(configure(sp, new())), optionsLifetime));
                break;
            case OptionsParameterType.DataOptionsTContext:
                builder.Services.TryAdd(new ServiceDescriptor(typeof(DataOptions<TContext>), sp => new DataOptions<TContext>(configure(sp, new())), optionsLifetime));
                break;
            case OptionsParameterType.DataOptions:
                builder.Services.TryAdd(new ServiceDescriptor(typeof(DataOptions), sp => configure(sp, new()), optionsLifetime));
                break;
        }

        return builder;
    }

    private static OptionsParameterType HasTypedContextConstructor<TContextImplementation, TContext>()
        where TContextImplementation : IDataContext
        where TContext : IDataContext {
        var constructors = typeof(TContextImplementation).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (constructors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(DataOptions<TContextImplementation>)))) {
            return OptionsParameterType.DataOptionsTImpl;
        }

        if (constructors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(DataOptions<TContext>)))) {
            return OptionsParameterType.DataOptionsTContext;
        }

        if (constructors.Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(DataOptions)))) {
            return OptionsParameterType.DataOptions;
        }

        throw new ArgumentException($"Missing constructor accepting '{nameof(DataOptions)}' on type {typeof(TContextImplementation).Name}.");
    }

    #region Nested type: OptionsParameterType

    private enum OptionsParameterType
    {
        DataOptionsTImpl,
        DataOptionsTContext,
        DataOptions,
    }

    #endregion
}
