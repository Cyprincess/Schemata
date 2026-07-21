using System.Collections.Generic;
using System.Linq;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Common;

/// <summary>Applies canonical resource-name predicates to resource query containers.</summary>
public static class ResourceIdentifiers
{
    /// <summary>Applies leaf-name and parent-scope predicates for a canonical resource name.</summary>
    public static void Apply<TEntity>(ResourceRequestContainer<TEntity> container, string? name)
        where TEntity : class, ICanonicalName {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ValidationException([new() {
                Field       = nameof(name),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), nameof(name).Humanize(LetterCasing.Title)),
                Reason      = SchemataResources.NOT_EMPTY,
            }]);
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parsed     = descriptor.ParseCanonicalName(name);
        if (parsed is null) {
            throw new ValidationException([new() {
                Field       = nameof(name),
                Description = LocalizedMessageFormatter.FormatInvariant(SchemataResources.INVALID_NAME, new Dictionary<string, string?> { ["name"] = name }),
                Reason      = SchemataResources.INVALID_NAME,
            }]);
        }

        var (parents, leaf) = parsed.Value;
        container.ApplyWhere(entity => entity.Name == leaf);
        container.ApplyWhere(descriptor.BuildParentPredicate<TEntity>(parents));
    }

    /// <summary>Applies a parent-scope predicate for an optional resource parent path.</summary>
    public static void ApplyParent<TEntity>(ResourceRequestContainer<TEntity> container, string? parent)
        where TEntity : class, ICanonicalName {
        if (string.IsNullOrWhiteSpace(parent)) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parsed     = descriptor.ParseParent(parent);
        if (parsed is null) {
            throw new ValidationException([new() {
                Field       = "parent",
                Description = LocalizedMessageFormatter.FormatInvariant(SchemataResources.INVALID_PARENT, new Dictionary<string, string?> { ["parent"] = parent }),
                Reason      = SchemataResources.INVALID_PARENT,
            }]);
        }

        if (!descriptor.SupportsReadAcross && parsed.Any(pair => pair.Value == "-")) {
            throw new ValidationException([new() {
                Field       = "parent",
                Description = LocalizedMessageFormatter.FormatInvariant(SchemataResources.CROSS_PARENT_UNSUPPORTED, new Dictionary<string, string?> { ["parent"] = parent }),
                Reason      = SchemataResources.CROSS_PARENT_UNSUPPORTED,
            }]);
        }

        container.ApplyWhere(descriptor.BuildParentPredicate<TEntity>(parsed));
    }
}
