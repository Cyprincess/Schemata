using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

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
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(name).Humanize(LetterCasing.Title)),
                Reason      = FieldReasons.NotEmpty,
            }]);
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var parsed     = descriptor.ParseCanonicalName(name);

        if (parsed is null) {
            throw new ValidationException([new() {
                Field       = nameof(name),
                Description = $"The requested resource name `{name}` is invalid.",
                Reason      = FieldReasons.InvalidName,
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
                Description = SchemataResources.GetResourceString(SchemataResources.ST2009),
                Reason      = FieldReasons.InvalidParent,
            }]);
        }

        if (!descriptor.SupportsReadAcross) {
            foreach (var kv in parsed) {
                if (kv.Value != "-") {
                    continue;
                }

                throw new ValidationException([new() {
                    Field       = nameof(ListRequest.Parent).Underscore(),
                    Description = SchemataResources.GetResourceString(SchemataResources.ST2002),
                    Reason      = FieldReasons.CrossParentUnsupported,
                }]);
            }
        }

        var predicate = descriptor.BuildParentPredicate<TEntity>(parsed);
        container.ApplyModification(predicate);
    }
}
