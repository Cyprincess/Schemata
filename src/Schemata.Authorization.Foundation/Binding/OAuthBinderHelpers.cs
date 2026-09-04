using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Schemata.Common;

namespace Schemata.Authorization.Foundation.Binding;

/// <summary>
///     Builds a property-to-parameter-name map by converting PascalCase property names to <c>snake_case</c> parameter
///     names.
/// </summary>
/// <remarks>
///     OAuth 2.0 and OIDC use <c>snake_case</c> parameter names (e.g. <c>client_id</c>, <c>response_type</c>).
///     This helper converts C# PascalCase properties to the conventional wire-format names.
/// </remarks>
public static class OAuthBinderHelpers
{
    /// <summary>
    ///     Builds a map of (PropertyInfo, snake_case parameter name) for all writable string and
    ///     string-collection properties on the given type.
    /// </summary>
    /// <param name="type">The request model type.</param>
    /// <returns>An array of tuples pairing each property with its corresponding wire-format parameter name.</returns>
    public static (PropertyInfo Prop, string Param)[] BuildMap(Type type) {
        return AppDomainTypeCache.GetWritableProperties(type)
                   .Where(p => p.PropertyType == typeof(string) || IsStringCollection(p))
                   .Select(p => (p, ToSnakeCase(p.Name)))
                   .ToArray();
    }

    /// <summary>
    ///     Binds one mapped property from raw parameter values. String properties take the single joined value;
    ///     collection-typed properties take every non-empty value, since such parameters may repeat
    ///     (<c>resource</c>, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html#section-2">
    ///         RFC 8707: Resource Indicators for OAuth 2.0 §2: Requesting a Resource
    ///     </seealso>
    ///     ).
    /// </summary>
    /// <param name="prop">The mapped model property.</param>
    /// <param name="values">The raw parameter values.</param>
    /// <param name="model">The request model instance being bound.</param>
    public static void Bind(PropertyInfo prop, StringValues values, object model) {
        if (IsStringCollection(prop)) {
            var items = new List<string>();
            foreach (var value in values) {
                if (!string.IsNullOrWhiteSpace(value)) {
                    items.Add(value);
                }
            }

            if (items.Count > 0) {
                prop.SetValue(model, items);
            }

            return;
        }

        var single = values.ToString();
        if (!string.IsNullOrWhiteSpace(single)) {
            prop.SetValue(model, single);
        }
    }

    private static bool IsStringCollection(PropertyInfo prop) {
        return typeof(ICollection<string>).IsAssignableFrom(prop.PropertyType);
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
    /// <summary>
    ///     Rejects request parameters provided more than once,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.1: Authorization Endpoint
    ///     </seealso>
    ///     and
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §3.2: Token Endpoint
    ///     </seealso>
    ///     . Collection-typed properties are exempt: their parameters may repeat (e.g. <c>resource</c>, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html#section-2">
    ///         RFC 8707: Resource Indicators for OAuth 2.0 §2: Requesting a Resource
    ///     </seealso>
    ///     .
    /// </summary>
    /// <param name="source">The raw request parameter store (query or form).</param>
    /// <param name="map">The property-to-parameter map for the bound request model.</param>
    public static void ThrowIfDuplicateParameters(
        IEnumerable<KeyValuePair<string, StringValues>> source,
        (PropertyInfo Prop, string Param)[] map) {
        var mapped = new HashSet<string>(
            map.Where(entry => !IsStringCollection(entry.Prop)).Select(entry => entry.Param));
        foreach (var (param, values) in source) {
            if (mapped.Contains(param) && values.Count > 1) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SINGLE), param));
            }
        }
    }
}
