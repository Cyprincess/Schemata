namespace Schemata.Authorization.Skeleton;

/// <summary>
///     One prepared back-channel recipient. Carries only addressing and identity facts; the
///     logout token is signed at execution time by the scheduled job.
/// </summary>
/// <param name="Uri">The relying party's back-channel logout URI.</param>
/// <param name="Audience">The logout token audience: the relying party's client_id.</param>
/// <param name="Subject">The recipient-specific subject identifier (pairwise when configured).</param>
/// <param name="SessionId">The OP session identifier, when the logout targets a session.</param>
/// <param name="SigningAlgorithm">
///     The negotiated logout-token signing algorithm, when the relying party registered one.
/// </param>
public sealed record BackChannelLogoutMessage(
    string  Uri,
    string? Audience,
    string? Subject,
    string? SessionId,
    string? SigningAlgorithm = null
);