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
    IReadOnlyList<Type> GetSummaryTypes(IServiceProvider serviceProvider);

    /// <summary>
    ///     Request / response DTOs that need trait field renames without a
    ///     <c>ListResultBase</c> wrapper. Defaults to an empty list.
    /// </summary>
    IReadOnlyList<Type> GetMessageTypes(IServiceProvider serviceProvider) => [];
}
