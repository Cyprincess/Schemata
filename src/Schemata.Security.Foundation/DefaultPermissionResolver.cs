using System;
using Humanizer;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation;

/// <summary>
///     Resolves AIP-style permissions as <c>{entity}.{operation}</c> in kebab-case.
///     E.g. <c>Resolve("Create", typeof(OrderItem))</c> produces <c>"order-item.create"</c>.
/// </summary>
public sealed class DefaultPermissionResolver : IPermissionResolver
{
    #region IPermissionResolver Members

    /// <inheritdoc />
    public string Resolve(string operation, Type entity) {
        return $"{entity.Name.Kebaberize()}.{operation.Kebaberize()}";
    }

    #endregion
}
