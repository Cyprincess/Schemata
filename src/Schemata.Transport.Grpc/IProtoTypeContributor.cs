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
    ///     Entity / summary types registered both standalone and wrapped as
    ///     <c>ListResultBase&lt;TSummary&gt;</c> per AIP-132.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <returns>The summary types to configure.</returns>
    IReadOnlyList<Type> GetSummaryTypes(IServiceProvider serviceProvider);

    /// <summary>
    ///     Request / response DTOs that need trait field renames without a
    ///     <c>ListResultBase</c> wrapper. Defaults to an empty list.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <returns>The message types to configure.</returns>
    IReadOnlyList<Type> GetMessageTypes(IServiceProvider serviceProvider) => [];
}
