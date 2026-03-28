using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Schemata.Authorization.Foundation.Binding;

internal static class OAuthBinderHelpers
{
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
