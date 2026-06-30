using System;
using System.Security.Cryptography;
using System.Text;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Identifies cached expression artifacts by language, source, target types, and compile options.
/// </summary>
public readonly struct ExpressionCacheKey : IEquatable<ExpressionCacheKey>
{
    private readonly string _value;

    private ExpressionCacheKey(string value) { _value = value; }

    /// <summary>
    ///     Creates a stable hash key for an expression compilation request.
    /// </summary>
    public static ExpressionCacheKey Create(
        string  language,
        string  source,
        Type?   contextType,
        Type?   resultType,
        string? options
    ) {
        var material = string.Join("\u001f", language, source, contextType?.AssemblyQualifiedName,
                                   resultType?.AssemblyQualifiedName, options);
        using var sha     = SHA256.Create();
        var       bytes   = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
        var       builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) {
            builder.Append(b.ToString("x2"));
        }

        return new(builder.ToString());
    }

    /// <summary>
    ///     Compares two cache keys by their hash value.
    /// </summary>
    public bool Equals(ExpressionCacheKey other) {
        return string.Equals(_value, other._value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) { return obj is ExpressionCacheKey other && Equals(other); }

    public override int GetHashCode() { return StringComparer.Ordinal.GetHashCode(_value); }

    public override string ToString() { return _value; }
}
