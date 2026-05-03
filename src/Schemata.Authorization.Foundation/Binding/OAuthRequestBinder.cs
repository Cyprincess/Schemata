using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Schemata.Authorization.Foundation.Binding;

/// <summary>
///     Builds a property-to-parameter-name map by converting PascalCase property names to <c>snake_case</c> parameter
///     names.
/// </summary>
/// <remarks>
///     OAuth 2.0 and OIDC use <c>snake_case</c> parameter names (e.g. <c>client_id</c>, <c>response_type</c>).
///     This helper converts C# PascalCase properties to the conventional wire-format names.
/// </remarks>
internal static class OAuthBinderHelpers
{
    /// <summary>
    ///     Builds a map of (PropertyInfo, snake_case parameter name) for all writable string properties on the given
    ///     type.
    /// </summary>
    /// <param name="type">The request model type.</param>
    /// <returns>An array of tuples pairing each property with its corresponding wire-format parameter name.</returns>
    public static (PropertyInfo Prop, string Param)[] BuildMap(Type type) {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Where(p => p.PropertyType == typeof(string) && p.CanWrite)
                   .Select(p => (p, ToSnakeCase(p.Name)))
                   .ToArray();
    }

    private static string ToSnakeCase(string name) {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++) {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) {
                if (char.IsLower(name[i - 1])) {
                    sb.Append('_');
                } else if (char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1])) {
                    sb.Append('_');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
