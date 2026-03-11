using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.RegularExpressions;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;

namespace Schemata.Common;

public sealed class ResourceNameDescriptor
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, ResourceNameDescriptor> Cache = new();
    private static readonly Regex PlaceholderRegex = new(@"\{(?<name>\w+)\}");

    private ResourceNameDescriptor(Type type) {
        var canonical = type.GetCustomAttribute<CanonicalNameAttribute>(false);

        Singular = type.GetCustomAttribute<DisplayNameAttribute>(false)?.DisplayName.Singularize()
                ?? type.GetCustomAttribute<TableAttribute>(false)?.Name.Singularize()
                ?? type.Name;

        Plural = Singular.Pluralize();

        Package = type.GetCustomAttribute<ResourcePackageAttribute>(false)?.Package;

        if (canonical is not null) {
            Pattern = canonical.ResourceName;

            var lastSlash   = Pattern.LastIndexOf('/');
            var lastSegment = lastSlash >= 0 ? Pattern.Substring(lastSlash + 1) : Pattern;

            // The last segment may be a placeholder like {book}, strip it to get the collection
            if (lastSegment.StartsWith("{")) {
                // Collection is the segment before the last placeholder
                var collectionEnd = lastSlash >= 0 ? lastSlash : 0;
                var prevSlash     = Pattern.LastIndexOf('/', collectionEnd - 1);
                Collection = prevSlash >= 0
                    ? Pattern.Substring(prevSlash + 1, collectionEnd - prevSlash - 1)
                    : Pattern.Substring(0, collectionEnd);
                CollectionPath = lastSlash > 0 ? Pattern.Substring(0, lastSlash) : Collection;
            } else {
                Collection     = lastSegment;
                CollectionPath = Pattern;
            }
        } else {
            Collection     = Plural.ToLowerInvariant();
            CollectionPath = Collection;
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

    public static ResourceNameDescriptor ForType(Type type) { return Cache.GetOrAdd(type.TypeHandle, _ => new(type)); }

    public static ResourceNameDescriptor ForType<T>() { return ForType(typeof(T)); }

    /// <summary>
    ///     Resolves placeholder values from an entity instance.
    ///     e.g., "publishers/{publisher}/books/{book}" + entity → "publishers/acme/books/les-miserables"
    /// </summary>
    public string Resolve(object entity) {
        if (Pattern is null) {
            throw new InvalidOperationException("Cannot resolve a resource name without a pattern.");
        }

        var type       = entity.GetType();
        var properties = AppDomainTypeCache.GetProperties(type);

        return PlaceholderRegex.Replace(Pattern, m => {
            var matched = m.Groups["name"].Value.Pascalize();

            var field = matched switch {
                "Parent"                                    => "Parent",
                var _ when string.Equals(Singular, matched) => "Name",
                var _                                       => $"{matched}Name",
            };

            if (!properties.TryGetValue(field, out var property)) {
                throw new MissingFieldException(type.Name, field);
            }

            var value = property.GetValue(entity)?.ToString();
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ValidationException([new(field, "not_empty")]);
            }

            return value;
        });
    }
}
