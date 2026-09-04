using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     The resource indicators adopted for the current token exchange, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html#section-2">
///         RFC 8707: Resource Indicators for OAuth 2.0 §2: Resource Parameter
///     </seealso>
///     . Published on the ambient advisor context at the token endpoint so claim advisors can
///     audience-restrict the issued tokens.
/// </summary>
public sealed record ResourceIndicators(IReadOnlyList<string> Values);
