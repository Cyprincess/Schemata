using System.Collections.Generic;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Translates an AIP-122 canonical name (or its parent prefix) into query predicates on a
///     <see cref="ResourceRequestContainer{TEntity}" />. Shared by the standard CRUD handler and the
///     AIP-136 custom-method handler.
/// </summary>
internal static class ResourceIdentifiers
{
    /// <summary>
    ///     Applies leaf-name and parent-scope predicates to the request container from a full
    ///     canonical name.
    /// </summary>
    /// <typeparam name="TEntity">The resource entity type.</typeparam>
    /// <param name="container">The request container receiving name and parent predicates.</param>
    /// <param name="name">The canonical resource name from the request.</param>
    /// <exception cref="ValidationException">The name is missing or malformed.</exception>
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
                Description = LocalizedMessageFormatter.FormatInvariant(
                    SchemataResources.INVALID_NAME,
                    new Dictionary<string, string?> { ["name"] = name }),
                Reason      = SchemataResources.INVALID_NAME,
            }]);
        }

        var (parents, leaf) = parsed.Value;

        container.ApplyModification(r => r.Name == leaf);

        var parent = descriptor.BuildParentPredicate<TEntity>(parents);
        container.ApplyModification(parent);
    }

    /// <summary>
    ///     Applies a parent-scope predicate to the request container from an AIP-159 parent path
    ///     (e.g. <c>publishers/acme</c>). An empty parent is a no-op so callers can pass through
    ///     optional <c>parent</c> request fields without an outer guard.
    /// </summary>
    /// <typeparam name="TEntity">The resource entity type.</typeparam>
    /// <param name="container">The request container receiving the parent predicate.</param>
    /// <param name="parent">The parent path from the request, or <see langword="null" />.</param>
    /// <exception cref="ValidationException">
    ///     The parent does not match the resource's pattern, or carries an AIP-159 wildcard on a
    ///     resource that does not opt in to <c>ReadAcrossAttribute</c>.
    /// </exception>
    public static void ApplyParent<TEntity>(ResourceRequestContainer<TEntity> container, string? parent)
        where TEntity : class, ICanonicalName {
        if (string.IsNullOrWhiteSpace(parent)) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parsed     = descriptor.ParseParent(parent);

        if (parsed is null) {
            throw new ValidationException([new() {
                Field       = nameof(ListRequest.Parent).Underscore(),
                Description = LocalizedMessageFormatter.FormatInvariant(
                    SchemataResources.INVALID_PARENT,
                    new Dictionary<string, string?> { ["parent"] = parent }),
                Reason      = SchemataResources.INVALID_PARENT,
            }]);
        }

        if (!descriptor.SupportsReadAcross) {
            foreach (var kv in parsed) {
                if (kv.Value != "-") {
                    continue;
                }

                throw new ValidationException([new() {
                    Field       = nameof(ListRequest.Parent).Underscore(),
                    Description = LocalizedMessageFormatter.FormatInvariant(
                        SchemataResources.CROSS_PARENT_UNSUPPORTED,
                        new Dictionary<string, string?> { ["parent"] = parent }),
                    Reason      = SchemataResources.CROSS_PARENT_UNSUPPORTED,
                }]);
            }
        }

        var predicate = descriptor.BuildParentPredicate<TEntity>(parsed);
        container.ApplyModification(predicate);
    }
}
