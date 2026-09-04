namespace Schemata.Authorization.Foundation.Commands;

/// <summary>AdviceContext carrier for the DPoP proof of a token request.</summary>
/// <param name="Value">Raw DPoP proof JWT header value, or <see langword="null" /> when absent.</param>
public sealed record DpopProof(string? Value);
