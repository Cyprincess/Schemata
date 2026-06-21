using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Humanizer;

namespace Schemata.Common;

/// <summary>
///     Resolves a wire/snake_case path segment to a member access on an expression.
/// </summary>
public static class MemberAccess
{
    /// <summary>
    ///     Resolves <paramref name="segment" /> to a property or field access on
    ///     <paramref name="source" />, matching the PascalCase member name.
    /// </summary>
    /// <param name="source">The expression whose member is accessed.</param>
    /// <param name="segment">The wire-format segment naming the member.</param>
    /// <returns>The member access, or <see langword="null" /> when no public instance member matches.</returns>
    public static Expression? Resolve(Expression source, string segment) {
        var member = source.Type.GetMember(segment.Pascalize(), BindingFlags.Instance | BindingFlags.Public)
                           .FirstOrDefault();
        return member switch {
            PropertyInfo property => Expression.Property(source, property),
            FieldInfo field       => Expression.Field(source, field),
            var _                 => null,
        };
    }
}
