using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Common;

/// <summary>
///     Parses and caches AIP-122 resource name patterns, providing methods for resolving, parsing, and building
///     canonical names.
///     See <seealso href="https://google.aip.dev/122">AIP-122: Resource names</seealso>
///     and <seealso href="https://google.aip.dev/159">AIP-159: Reading across collections</seealso>.
/// </summary>
public sealed class ResourceNameDescriptor
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, ResourceNameDescriptor> Cache = new();

    private readonly Segment?  _leafSegment;
    private readonly Segment[] _parentSegments;
    private readonly Segment[] _segments;

    private ResourceNameDescriptor(Type type) {
        var canonical = type.GetCustomAttribute<CanonicalNameAttribute>(false);

        Singular = type.GetCustomAttribute<DisplayNameAttribute>(false)?.DisplayName.Singularize()
                ?? type.GetCustomAttribute<TableAttribute>(false)?.Name.Singularize()
                ?? type.Name;

        Plural = Singular.Pluralize();

        Package = type.GetCustomAttribute<ResourcePackageAttribute>(false)?.Package;

        SupportsReadAcross = type.GetCustomAttribute<ReadAcrossAttribute>(false) is not null;

        if (canonical is null) {
            Collection      = Plural.ToLowerInvariant();
            CollectionPath  = Collection;
            _segments       = [];
            _parentSegments = [];
            return;
        }

        Pattern = canonical.ResourceName;

        var parts = Pattern.Split('/');
        _segments = new Segment[parts.Length];
        for (var i = 0; i < parts.Length; i++) {
            var p = parts[i];
            if (p.Length > 2 && p[0] == '{' && p[p.Length - 1] == '}') {
                var name = p.Substring(1, p.Length - 2);
                _segments[i] = new(p, name, ResolvePropertyName(name.Pascalize()));
            } else {
                _segments[i] = new(p, null, null);
            }
        }

        var leaf = _segments[_segments.Length - 1];
        if (leaf.IsPlaceholder) {
            _leafSegment   = leaf;
            Collection     = _segments.Length >= 2 ? _segments[_segments.Length - 2].Raw : "";
            CollectionPath = string.Join("/", SliceSegments(_segments, 0, _segments.Length - 1).Select(s => s.Raw));
        } else {
            _leafSegment   = null;
            Collection     = leaf.Raw;
            CollectionPath = Pattern;
        }

        var placeholderCount = _segments.Count(s => s.IsPlaceholder);
        if (placeholderCount <= 1) {
            _parentSegments = Array.Empty<Segment>();
        } else if (leaf.IsPlaceholder) {
            _parentSegments = SliceSegments(_segments, 0, _segments.Length - 2);
        } else {
            _parentSegments = SliceSegments(_segments, 0, _segments.Length - 1);
        }

        return;

        string ResolvePropertyName(string placeholder) {
            return placeholder switch {
                "Parent"                                        => "Parent",
                var _ when string.Equals(Singular, placeholder) => "Name",
                var _                                           => $"{placeholder}Name",
            };
        }
    }

    /// <summary>
    ///     The full <seealso href="https://google.aip.dev/122">AIP-122: Resource names</seealso> pattern, e.g.,
    ///     <c>"publishers/{publisher}/books/{book}"</c>.
    ///     <see langword="null" /> when no <see cref="CanonicalNameAttribute" /> is present.
    /// </summary>
    public string? Pattern { get; }

    /// <summary>
    ///     PascalCase singular form derived from <see cref="DisplayNameAttribute" />, <see cref="TableAttribute" />, or
    ///     the type name.
    /// </summary>
    public string Singular { get; }

    /// <summary>
    ///     PascalCase plural form.
    /// </summary>
    public string Plural { get; }

    /// <summary>
    ///     Last collection segment from the pattern, e.g., <c>"books"</c>.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    ///     Everything up to and including the last collection segment, e.g.,
    ///     <c>"publishers/{publisher}/books"</c> — used for HTTP routing.
    /// </summary>
    public string CollectionPath { get; }

    /// <summary>
    ///     API package/prefix from <see cref="ResourcePackageAttribute" />, used as route prefix and gRPC service name
    ///     prefix. <see langword="null" /> when no attribute is present.
    /// </summary>
    public string? Package { get; }

    /// <summary>
    ///     <see langword="true" /> when the resource has parent segments in its pattern.
    /// </summary>
    public bool HasParent => _parentSegments.Length > 0;

    /// <summary>
    ///     <see langword="true" /> when the entity type has <see cref="ReadAcrossAttribute" /> (
    ///     <seealso href="https://google.aip.dev/159">AIP-159: Reading across collections</seealso> opt-in).
    /// </summary>
    public bool SupportsReadAcross { get; }

    /// <summary>
    ///     Gets or creates a cached descriptor for the specified entity type.
    /// </summary>
    /// <param name="type">The entity type.</param>
    /// <returns>The resource name descriptor.</returns>
    public static ResourceNameDescriptor ForType(Type type) { return Cache.GetOrAdd(type.TypeHandle, _ => new(type)); }

    /// <summary>
    ///     Gets or creates a cached descriptor for the specified entity type.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <returns>The resource name descriptor.</returns>
    public static ResourceNameDescriptor ForType<T>() { return ForType(typeof(T)); }

    /// <summary>
    ///     Resolves placeholder values from an entity instance against the pattern.
    ///     e.g., <c>"publishers/{publisher}/books/{book}"</c> + entity => <c>"publishers/acme/books/les-miserables"</c>.
    /// </summary>
    /// <param name="entity">The entity instance.</param>
    /// <returns>The resolved resource name.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no pattern is defined.</exception>
    /// <exception cref="MissingFieldException">Thrown when a required property is not found on the entity.</exception>
    /// <exception cref="ValidationException">Thrown when a required placeholder value is missing or empty.</exception>
    public string Resolve(object entity) {
        if (_segments.Length == 0) {
            throw new InvalidOperationException(SchemataResources.GetResourceString(SchemataResources.ST1018));
        }

        var type       = entity.GetType();
        var properties = AppDomainTypeCache.GetProperties(type);
        var parts      = new string[_segments.Length];
        for (var i = 0; i < _segments.Length; i++) {
            var seg = _segments[i];
            if (!seg.IsPlaceholder) {
                parts[i] = seg.Raw;
                continue;
            }

            if (!properties.TryGetValue(seg.Property!, out var property)) {
                throw new MissingFieldException(type.Name, seg.Property!);
            }

            var value = property.GetValue(entity)?.ToString();
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ValidationException([new() {
                    Field       = seg.Property!.Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), seg.Property!.Humanize(LetterCasing.Title)),
                    Reason      = FieldReasons.NotEmpty,
                }]);
            }

            parts[i] = value!;
        }

        return string.Join("/", parts);
    }

    private static Dictionary<string, string>? MatchSegments(Segment[] pattern, string[] input) {
        if (pattern.Length != input.Length) return null;
        var values = new Dictionary<string, string>();
        for (var i = 0; i < pattern.Length; i++) {
            if (pattern[i].IsPlaceholder) {
                values[pattern[i].Placeholder!] = input[i];
            } else if (!string.Equals(pattern[i].Raw, input[i], StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
        }

        return values;
    }

    /// <summary>
    ///     Parses a parent string using the parent portion of <see cref="CollectionPath" />.
    ///     e.g., <c>"publishers/acme"</c> with CollectionPath <c>"publishers/{publisher}/books"</c>
    ///     produces <c>{ "publisher": "acme" }</c>.
    /// </summary>
    /// <param name="parent">The parent path to parse.</param>
    /// <returns>A dictionary of placeholder names to values, or <see langword="null" /> if parsing fails.</returns>
    public Dictionary<string, string>? ParseParent(string? parent) {
        if (_parentSegments.Length == 0 || string.IsNullOrWhiteSpace(parent)) {
            return null;
        }

        return MatchSegments(_parentSegments, parent!.Split('/'));
    }

    /// <summary>
    ///     Parses a full canonical name against <see cref="Pattern" /> and returns parent values plus the leaf name.
    ///     e.g., <c>"publishers/acme/books/les-miserables"</c> =>
    ///     <c>({"publisher": "acme"}, "les-miserables")</c>.
    /// </summary>
    /// <param name="canonicalName">The full canonical resource name.</param>
    /// <returns>A tuple of parent values and leaf name, or <see langword="null" /> if parsing fails.</returns>
    public (Dictionary<string, string> ParentValues, string LeafName)? ParseCanonicalName(string canonicalName) {
        if (_segments.Length == 0 || _leafSegment is not { IsPlaceholder: true } leaf) {
            return null;
        }

        var all = MatchSegments(_segments, canonicalName.Split('/'));
        if (all is null || !all.TryGetValue(leaf.Placeholder!, out var leafName)) {
            return null;
        }

        all.Remove(leaf.Placeholder!);

        return (all, leafName);
    }

    /// <summary>
    ///     Builds a parent path string from ASP.NET route values.
    ///     e.g., routeValues <c>{ publisher: "acme" }</c> => <c>"publishers/acme"</c>.
    /// </summary>
    /// <param name="routeValues">The route value dictionary.</param>
    /// <returns>The parent path, or <see langword="null" /> if no parent segments exist.</returns>
    public string? ResolveParent(IDictionary<string, object?> routeValues) {
        if (_parentSegments.Length == 0) return null;
        var parts = new string[_parentSegments.Length];
        for (var i = 0; i < _parentSegments.Length; i++) {
            var seg = _parentSegments[i];
            if (seg.IsPlaceholder && routeValues.TryGetValue(seg.Placeholder!, out var value) && value is string text) {
                parts[i] = text;
            } else {
                parts[i] = seg.Raw;
            }
        }

        return string.Join("/", parts);
    }

    /// <summary>
    ///     Extracts parent placeholder values from route values.
    /// </summary>
    /// <param name="routeValues">The route value dictionary.</param>
    /// <returns>A dictionary of placeholder names to values, or <see langword="null" /> if none found.</returns>
    public Dictionary<string, string>? ExtractParentValues(IDictionary<string, object?> routeValues) {
        if (_parentSegments.Length == 0) return null;
        var values = new Dictionary<string, string>();
        foreach (var seg in _parentSegments) {
            if (seg.IsPlaceholder && routeValues.TryGetValue(seg.Placeholder!, out var value) && value is string text) {
                values[seg.Placeholder!] = text;
            }
        }

        return values.Count > 0 ? values : null;
    }

    /// <summary>
    ///     Sets parent properties on the target object from route values.
    /// </summary>
    /// <param name="target">The target object.</param>
    /// <param name="routeValues">The route value dictionary.</param>
    public void SetParentFromRouteValues(object target, IDictionary<string, object?> routeValues) {
        if (_parentSegments.Length == 0) return;
        var properties = AppDomainTypeCache.GetProperties(target.GetType());
        foreach (var seg in _parentSegments) {
            if (!seg.IsPlaceholder) continue;
            if (routeValues.TryGetValue(seg.Placeholder!, out var value) && value is string text) {
                if (properties.TryGetValue(seg.Property!, out var property)) {
                    property.SetValue(target, text);
                }
            }
        }
    }

    /// <summary>
    ///     Builds a WHERE predicate expression from parent values.
    ///     Skips predicates where the value is <c>"-"</c> (
    ///     <seealso href="https://google.aip.dev/159">AIP-159: Reading across collections</seealso> wildcard).
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="parentValues">The parent values dictionary.</param>
    /// <returns>A predicate expression, or <see langword="null" /> if no conditions can be built.</returns>
    public Expression<Func<T, bool>>? BuildParentPredicate<T>(Dictionary<string, string> parentValues) {
        var parameter  = Expression.Parameter(typeof(T), "e");
        var properties = AppDomainTypeCache.GetProperties(typeof(T));

        Expression? body = null;

        foreach (var seg in _parentSegments) {
            if (!seg.IsPlaceholder) continue;

            if (!parentValues.TryGetValue(seg.Placeholder!, out var value)) {
                continue;
            }

            // AIP-159: Reading across collections — skip wildcard parent
            if (value == "-") {
                continue;
            }

            if (!properties.TryGetValue(seg.Property!, out var property)) {
                continue;
            }

            var member   = Expression.Property(parameter, property);
            var constant = Expression.Constant(value, typeof(string));
            var equal    = Expression.Equal(member, constant);

            body = body is null ? equal : Expression.AndAlso(body, equal);
        }

        if (body is null) {
            return null;
        }

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    ///     Clears parent properties on the target object (sets them to <see langword="null" />).
    /// </summary>
    /// <param name="target">The target object.</param>
    public void ClearParentProperties(object target) {
        var properties = AppDomainTypeCache.GetProperties(target.GetType());

        foreach (var seg in _parentSegments) {
            if (!seg.IsPlaceholder) continue;
            if (!properties.TryGetValue(seg.Property!, out var property)) continue;
            property.SetValue(target, null);
        }
    }

    private static Segment[] SliceSegments(Segment[] source, int start, int count) {
        if (count <= 0) return Array.Empty<Segment>();
        var result = new Segment[count];
        Array.Copy(source, start, result, 0, count);
        return result;
    }

    #region Nested type: Segment

    private readonly struct Segment
    {
        public Segment(string raw, string? placeholder, string? property) {
            Raw         = raw;
            Placeholder = placeholder;
            Property    = property;
        }

        public string  Raw           { get; }
        public string? Placeholder   { get; }
        public string? Property      { get; }
        public bool    IsPlaceholder => Placeholder is not null;
    }

    #endregion
}
