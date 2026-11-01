using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     The device secret issued or reused during a token grant: the secret reference the
///     response carries, the binding facts the prepared token row persists, and — for a new
///     issuance — the prepared row itself, published atomically with the issued token family.
/// </summary>
/// <param name="DeviceSecret">The device secret reference returned to the client.</param>
/// <param name="DeviceId">The resolved device the secret is bound to.</param>
/// <param name="SourceClientId">The client the secret was originally issued for.</param>
/// <param name="SessionId">The native session the secret participates in.</param>
/// <param name="Family">The token family the secret joins.</param>
/// <param name="PreparedToken">The prepared device-secret row for a new issuance; <see langword="null" /> for a reuse.</param>
public sealed record DeviceSecretIssuance(
    string         DeviceSecret,
    string?        DeviceId,
    string?        SourceClientId,
    string?        SessionId,
    string?        Family        = null,
    SchemataToken? PreparedToken = null
);
