using System;
using System.Collections.Generic;

namespace Schemata.Transport.Grpc;

/// <summary>
///     Contributes types to <c>SchemataTransportGrpcFeature</c>, which applies the
///     Schemata wire conventions to each one against <c>RuntimeTypeModel.Default</c>
///     on application startup.
/// </summary>
public interface IProtoTypeContributor
{
    /// <summary>
    ///     Identity / item type pairs whose item is registered standalone and whose
    ///     <c>ListResultBase&lt;TEntity, TItem&gt;</c> wrapper is registered per AIP-132.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <returns>The list type pairs to configure.</returns>
    IReadOnlyList<(Type Identity, Type Item)> GetListTypes(IServiceProvider serviceProvider);

    /// <summary>
    ///     Request / response DTOs that need trait field renames without a
    ///     <c>ListResultBase</c> wrapper. Defaults to an empty list.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <returns>The message types to configure.</returns>
    IReadOnlyList<Type> GetMessageTypes(IServiceProvider serviceProvider) => [];
}
