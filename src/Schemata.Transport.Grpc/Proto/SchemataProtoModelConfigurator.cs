using System;
using System.Collections.Generic;
using System.Linq;
using Humanizer;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Transport.Grpc.Proto;

/// <summary>
///     Configures a <see cref="RuntimeTypeModel" /> with the Schemata wire conventions:
///     property names are resolved through <see cref="ResourceWireNameRules" /> and emitted
///     in snake-case via <see cref="InflectorExtensions.Underscore" />.
/// </summary>
public static class SchemataProtoModelConfigurator
{
    /// <summary>
    ///     Registers <paramref name="type" /> on <paramref name="model" /> with trait-aware
    ///     field names. Idempotent across already-configured and cyclic type graphs. Returns
    ///     <see langword="false" /> when the type cannot be added (e.g. open generic).
    /// </summary>
    /// <remarks>
    ///     Dictionary properties with a scalar key are registered as proto3 maps
    ///     (<see cref="ValueMember.IsMap" />), so duplicate keys resolve last-wins. A
    ///     <see langword="null" /> map value is written as a key-only entry on the wire;
    ///     proto3 readers materialize it as an empty string.
    /// </remarks>
    public static bool ConfigureType(RuntimeTypeModel model, Type? type) {
        lock (model) {
            return ConfigureType(model, type, []);
        }
    }

    private static bool ConfigureType(RuntimeTypeModel model, Type? type, HashSet<Type> configuring) {
        if (type is null) {
            return false;
        }

        if (type is { IsGenericType: true, IsConstructedGenericType: false }) {
            return false;
        }

        if (!configuring.Add(type)) {
            return true;
        }

        if (model.IsDefined(type)) {
            configuring.Remove(type);
            return true;
        }

        try {
            MetaType meta;
            try {
                meta = model.Add(type, true);
            } catch (ArgumentException) when (model.IsDefined(type)) {
                return true;
            }

            var properties = AppDomainTypeCache.GetWritableProperties(type).ToList();

            var number = 1;
            foreach (var property in properties) {
                var resolved = ResourceWireNameRules.ResolveWireName(type, property.Name);
                if (resolved is null) {
                    continue;
                }

                if (meta.GetFields().Any(f => f.Name == property.Name)) {
                    continue;
                }

                var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                ConfigureDependencies(model, underlying, configuring);

                var field = meta.AddField(number++, property.Name);
                field.Name = resolved.Underscore();

                var key = GetDictionaryKeyType(property.PropertyType);
                if (key is not null && IsScalarMapKey(key)) {
                    field.IsMap = true;
                }
            }

            return true;
        } finally {
            configuring.Remove(type);
        }
    }

    private static void ConfigureDependencies(RuntimeTypeModel model, Type underlying, HashSet<Type> configuring) {
        if (underlying.IsGenericType) {
            foreach (var argument in underlying.GetGenericArguments()) {
                if (argument.IsClass && argument != typeof(string)) {
                    ConfigureType(model, argument, configuring);
                }
            }
        }

        if (underlying.IsClass && underlying != typeof(string) && !underlying.IsArray && !IsNativeContainer(underlying)) {
            ConfigureType(model, underlying, configuring);
        }
    }

    private static bool IsNativeContainer(Type type) {
        if (!type.IsGenericType) {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IDictionary<,>)
            || definition == typeof(IEnumerable<>)
            || type.GetInterfaces().Any(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                                                               || i.GetGenericTypeDefinition() == typeof(IEnumerable<>)));
    }

    private static Type? GetDictionaryKeyType(Type type) {
        var dictionary = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
            ? type
            : type.GetInterfaces()
                  .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        return dictionary?.GetGenericArguments()[0];
    }

    private static bool IsScalarMapKey(Type type) {
        return type == typeof(string)
            || type == typeof(bool)
            || type == typeof(sbyte)
            || type == typeof(byte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(char);
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
