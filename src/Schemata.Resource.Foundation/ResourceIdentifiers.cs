using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Translates an AIP-122 canonical name into query predicates (leaf name plus parent
///     scope) on a <see cref="ResourceRequestContainer{TEntity}" />. Shared by the standard
///     CRUD handler and the AIP-136 custom-method handler.
/// </summary>
internal static class ResourceIdentifiers
{
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
}
