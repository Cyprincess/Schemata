using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Schemata.Entity.Repository;

/// <summary>
///     Builds the shared provider cache-key fragment for repository queries. The structural
///     representation of a command (provider name, data-source identity, command text, and
///     ordered typed parameters) is hashed with SHA-256 over its UTF-16 ordinal encoding, so
///     the returned key does not expose connection identity, command text, or parameter values.
/// </summary>
/// <remarks>
///     Parameter values are encoded by runtime type. Supported values are
///     <see langword="null" />, <see cref="bool" />, <see cref="char" />, the fixed-width
///     integral types, <see cref="float" />, <see cref="double" />, <see cref="decimal" />,
///     <see cref="string" />, <see cref="Guid" />, <see cref="DateTime" />,
///     <see cref="DateTimeOffset" />, <see cref="TimeSpan" />, enums, <see cref="byte" />
///     arrays, and single-dimensional zero-based arrays whose element type is one of those
///     scalars. Any other value — including custom <see cref="IEnumerable" /> implementations —
///     makes <see cref="Create" /> return <see langword="null" />. Values are not stringized
///     through user code and custom enumerables are not enumerated.
/// </remarks>
public static class QueryCacheKey
{
    /// <summary>
    ///     Returns the hashed cache-key fragment for the given command, or
    ///     <see langword="null" /> when any component is empty or any parameter value has an
    ///     unsupported runtime type.
    /// </summary>
    /// <param name="provider">The stable short name of the query provider, e.g. <c>efcore</c>.</param>
    /// <param name="source">
    ///     The raw data-source identity: endpoint, database, credentials, session settings. The
    ///     value participates only in the internal SHA-256 hash and does not appear in the
    ///     returned key, so different sources produce different keys.
    /// </param>
    /// <param name="command">The command text the provider would execute.</param>
    /// <param name="parameters">The ordered command parameters: name, database type, and value.</param>
    /// <returns>The hashed key, or <see langword="null" /> when the command is not keyable.</returns>
    public static string? Create(
        string            provider,
        string            source,
        string            command,
        IEnumerable<(string Name, string Type, object? Value)> parameters
    ) {
        if (string.IsNullOrEmpty(provider)
         || string.IsNullOrEmpty(source)
         || string.IsNullOrEmpty(command)
         || parameters is null) {
            return null;
        }

        var material = new StringBuilder();

        material.Append("qckv1\x1e");
        AppendField(material, provider);
        material.Append('\x1e');
        AppendField(material, source);
        material.Append('\x1e');
        AppendField(material, command);

        foreach (var (name, type, value) in parameters) {
            material.Append('\x1e');
            AppendField(material, name);
            material.Append('\x1e');
            AppendField(material, type);
            material.Append('\x1e');

            if (!AppendValue(material, value)) {
                return null;
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(material.ToString())));
    }

    /// <summary>
    ///     Appends the value prefixed by its character count, so separator characters embedded
    ///     in the content cannot make distinct commands collide.
    /// </summary>
    private static void AppendField(StringBuilder material, string value) {
        material.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }

    private static bool AppendValue(StringBuilder material, object? value) {
        switch (value) {
            case null:
                material.Append('N');
                return true;
            case bool flag:
                material.Append(flag ? "b1" : "b0");
                return true;
            case char c:
                material.Append('c').Append(((int)c).ToString(CultureInfo.InvariantCulture));
                return true;
            case sbyte v:
                material.Append("i1:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case short v:
                material.Append("i2:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case int v:
                material.Append("i4:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case long v:
                material.Append("i8:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case byte v:
                material.Append("u1:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case ushort v:
                material.Append("u2:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case uint v:
                material.Append("u4:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case ulong v:
                material.Append("u8:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case decimal v:
                material.Append("d:").Append(v.ToString(CultureInfo.InvariantCulture));
                return true;
            case double v:
                material.Append("f8:").Append(v.ToString("R", CultureInfo.InvariantCulture));
                return true;
            case float v:
                material.Append("f4:").Append(v.ToString("R", CultureInfo.InvariantCulture));
                return true;
            case string v:
                material.Append('s');
                AppendField(material, v);
                return true;
            case Guid v:
                material.Append('g').Append(v.ToString("N"));
                return true;
            case DateTime v:
                material.Append("dt").Append(v.Ticks.ToString(CultureInfo.InvariantCulture)).Append(':').Append(((int)v.Kind).ToString(CultureInfo.InvariantCulture));
                return true;
            case DateTimeOffset v:
                material.Append("do").Append(v.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append(':').Append(v.Offset.Ticks.ToString(CultureInfo.InvariantCulture));
                return true;
            case TimeSpan v:
                material.Append("ts").Append(v.Ticks.ToString(CultureInfo.InvariantCulture));
                return true;
            case byte[] v:
                material.Append('y').Append(v.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Convert.ToHexString(v));
                return true;
            case Array v:
                return AppendArray(material, v);
            case Enum v:
                AppendEnum(material, v);
                return true;
            default:
                return false;
        }
    }

    private static bool AppendArray(StringBuilder material, Array array) {
        var element = array.GetType().GetElementType();
        if (array.Rank != 1 || array.GetLowerBound(0) != 0 || element is null || !IsScalarElementType(element)) {
            return false;
        }

        material.Append('a');
        AppendField(material, element.FullName ?? element.Name);
        material.Append(':').Append(array.Length.ToString(CultureInfo.InvariantCulture)).Append(":[");
        for (var i = 0; i < array.Length; i++) {
            if (i > 0) {
                material.Append(',');
            }

            if (!AppendValue(material, array.GetValue(i))) {
                return false;
            }
        }

        material.Append(']');
        return true;
    }

    private static void AppendEnum(StringBuilder material, Enum value) {
        var type = value.GetType();

        material.Append('e');
        AppendField(material, type.FullName ?? type.Name);
        material.Append(':');
        switch (value.GetTypeCode()) {
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
                material.Append("i8:").Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture));
                break;
            default:
                material.Append("u8:").Append(Convert.ToUInt64(value).ToString(CultureInfo.InvariantCulture));
                break;
        }
    }

    private static bool IsScalarElementType(Type type) {
        return type == typeof(bool)
            || type == typeof(char)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(byte)
            || type == typeof(ushort)
            || type == typeof(uint)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(string)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type.IsEnum;
    }
}
