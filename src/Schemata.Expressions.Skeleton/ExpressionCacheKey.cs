using System;
using System.Security.Cryptography;
using System.Text;

namespace Schemata.Expressions.Skeleton;

public readonly struct ExpressionCacheKey : IEquatable<ExpressionCacheKey>
{
    private readonly string _value;

    private ExpressionCacheKey(string value) { _value = value; }

    public static ExpressionCacheKey Create(
        string  language,
        string  source,
        Type?   contextType,
        Type?   resultType,
        string? options
    ) {
        var material = string.Join("\u001f", language, source, contextType?.AssemblyQualifiedName,
                                   resultType?.AssemblyQualifiedName, options ?? string.Empty);
        using var sha     = SHA256.Create();
        var       bytes   = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
        var       builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) {
            builder.Append(b.ToString("x2"));
        }

        return new(builder.ToString());
    }

    public bool Equals(ExpressionCacheKey other) {
        return string.Equals(_value, other._value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) { return obj is ExpressionCacheKey other && Equals(other); }

    public override int GetHashCode() { return StringComparer.Ordinal.GetHashCode(_value); }

    public override string ToString() { return _value; }
}
