using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Transport.Grpc.Proto;

/// <summary>
///     Configures a <see cref="RuntimeTypeModel" /> with the Schemata wire conventions:
///     property names are resolved through <see cref="SchemataProtoTraits" /> and emitted
///     in snake-case via <see cref="SchemataNaming.ToWireName" />.
/// </summary>
public static class SchemataProtoModelConfigurator
{
    /// <summary>
    ///     Registers <paramref name="type" /> on <paramref name="model" /> with trait-aware
    ///     field names. Idempotent across already-configured types and fields. Returns
    ///     <see langword="false" /> when the type cannot be added (e.g. open generic).
    /// </summary>
    public static bool ConfigureType(RuntimeTypeModel model, Type? type) {
        if (type is null) {
            return false;
        }

        if (model.CanSerialize(type)) {
            return true;
        }

        if (type is { IsGenericType: true, IsConstructedGenericType: false }) {
            return false;
        }

        var meta = model.Add(type, true);

        var properties = AppDomainTypeCache.GetWritableProperties(type).ToList();

        var number = 1;
        foreach (var property in properties) {
            var resolved = SchemataProtoTraits.ResolveWireName(type, property.Name);
            if (resolved is null) {
                continue;
            }

            if (meta.GetFields().Any(f => f.Name == property.Name)) {
                continue;
            }

            var field = meta.AddField(number++, property.Name);
            field.Name = SchemataNaming.ToWireName(resolved);

            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (underlying.IsClass && underlying != typeof(string) && !underlying.IsArray) {
                ConfigureType(model, underlying);
            }

            if (!underlying.IsGenericType) {
                continue;
            }

            foreach (var argument in underlying.GetGenericArguments()) {
                if (argument.IsClass && argument != typeof(string)) {
                    ConfigureType(model, argument);
                }
            }
        }

        return true;
    }

    /// <summary>
    ///     Registers <see cref="ListResultBase{TSummary}" /> for the given
    ///     <paramref name="summary" /> on <paramref name="model" />.
    /// </summary>
    public static void ConfigureListResultType(RuntimeTypeModel model, Type summary) {
        var response = typeof(ListResultBase<>).MakeGenericType(summary);
        ConfigureType(model, response);
    }

    /// <summary>
    ///     Registers each summary type and its <see cref="ListResultBase{TSummary}" />
    ///     wrapper on <paramref name="model" />.
    /// </summary>
    public static void ConfigureSummaryTypes(RuntimeTypeModel model, IEnumerable<Type> summaryTypes) {
        foreach (var type in summaryTypes) {
            ConfigureType(model, type);
            ConfigureListResultType(model, type);
        }
    }
}
