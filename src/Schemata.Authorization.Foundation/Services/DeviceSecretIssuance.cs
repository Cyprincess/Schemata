using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Services;

internal sealed record DeviceSecretIssuance(
    string         DeviceSecret,
    string?        DeviceId,
    string?        SourceClientId,
    string?        SessionId,
    string?        Family        = null,
    SchemataToken? PreparedToken = null
);