using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Authentication context asserted for a principal: <paramref name="Acr" /> names the context
///     class the authentication satisfied, <paramref name="Amr" /> lists the RFC 8176 method
///     references used, and <paramref name="AuthTime" /> is the Unix-seconds timestamp of the
///     authentication event (OpenID Connect Core 1.0 §2).
/// </summary>
/// <param name="Acr">Satisfied Authentication Context Class Reference, or <c>null</c> when unknown.</param>
/// <param name="Amr">Authentication method references; empty when unknown.</param>
/// <param name="AuthTime">Authentication event time in Unix seconds, or <c>null</c> when unknown.</param>
public sealed record AuthenticationContext(string? Acr, IReadOnlyList<string> Amr, long? AuthTime);