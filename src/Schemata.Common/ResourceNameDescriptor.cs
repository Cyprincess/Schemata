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
///     Parses and caches AIP-122 resource name patterns, providing methods for resolving, parsing, and building canonical names.
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
    ///     The full AIP-122 pattern, e.g., "publishers/{publisher}/books/{book}".
    ///     Null when no <see cref="CanonicalNameAttribute" /> is present.
    /// </summary>
    public string? Pattern { get; }

    /// <summary>
    ///     PascalCase singular, e.g., "Book" (from DisplayName/Table/TypeName).
    /// </summary>
    public string Singular { get; }

    /// <summary>
    ///     PascalCase plural, e.g., "Books".
    /// </summary>
    public string Plural { get; }

    /// <summary>
    ///     Collection identifier (last collection segment from pattern), e.g., "books".
    /// </summary>
    public string Collection { get; }

    /// <summary>
    ///     For HTTP routing: everything up to and including the last collection segment,
    ///     e.g., "publishers/{publisher}/books".
    /// </summary>
    public string CollectionPath { get; }

    /// <summary>
    ///     API package/prefix. Read from <see cref="ResourcePackageAttribute" /> on the type.
    ///     Used by HTTP as route prefix and by gRPC as service name prefix.
    ///     Null when no attribute is present.
    /// </summary>
    public string? Package { get; }

    /// <summary>
    ///     True when the resource has parent segments in its pattern.
    /// </summary>
    public bool HasParent => _parentSegments.Length > 0;

    /// <summary>
    ///     True when the entity type has <see cref="ReadAcrossAttribute" /> (AIP-159 opt-in).
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
    ///     Resolves placeholder values from an entity instance.
    ///     e.g., "publishers/{publisher}/books/{book}" + entity → "publishers/acme/books/les-miserables"
    /// </summary>
    public string Resolve(object entity) {
        if (_segments.Length == 0) {
            throw new InvalidOperationException("Cannot resolve a resource name without a pattern.");
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
                throw new ValidationException([
                    new() {
                        Field       = seg.Property!,
                        Reason      = FieldReasons.Required,
                        Description = SchemataResources.GetResourceString(SchemataResources.ST1010),
                    },
                ]);
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
    ///     Parse a parent string using the parent portion of CollectionPath.
    ///     e.g., "publishers/acme" with CollectionPath "publishers/{publisher}/books" → { "publisher": "acme" }
    /// </summary>
    public Dictionary<string, string>? ParseParent(string? parent) {
        if (_parentSegments.Length == 0 || string.IsNullOrWhiteSpace(parent)) {
            return null;
        }

        return MatchSegments(_parentSegments, parent!.Split('/'));
    }

    /// <summary>
    ///     Parse full canonical name using Pattern.
    ///     e.g., "publishers/acme/books/les-miserables" → ({"publisher": "acme"}, "les-miserables")
    /// </summary>
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
    ///     Build parent string from ASP.NET route values.
    ///     e.g., routeValues { publisher: "acme" } → "publishers/acme"
    /// </summary>
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
    ///     Extract parent placeholder values from route values.
    /// </summary>
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
    ///     Set parent properties on target from route values.
    /// </summary>
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
    ///     Build WHERE predicate from parent values.
    ///     Skips predicates where value is "-" (AIP-159 wildcard).
    /// </summary>
    public Expression<Func<T, bool>>? BuildParentPredicate<T>(Dictionary<string, string> parentValues) {
        var parameter  = Expression.Parameter(typeof(T), "e");
        var properties = AppDomainTypeCache.GetProperties(typeof(T));

        Expression? body = null;

        foreach (var seg in _parentSegments) {
            if (!seg.IsPlaceholder) continue;

            if (!parentValues.TryGetValue(seg.Placeholder!, out var value)) {
                continue;
            }

            // AIP-159: skip wildcard parent
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
    ///     Clear parent properties on object (set to null).
    /// </summary>
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
