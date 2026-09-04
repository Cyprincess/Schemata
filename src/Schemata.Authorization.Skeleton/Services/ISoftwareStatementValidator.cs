using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Validates software statements presented during dynamic registration, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7591.html#section-2.3">
///         RFC 7591: OAuth 2.0 Dynamic Client
///         Registration Protocol §2.3: Software Statement
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     The authorization server's trust anchor for software statement issuers. Hosts implement
///     this interface to trust issuers; without a registration every presented statement is
///     rejected with <c>unapproved_software_statement</c>.
/// </remarks>
public interface ISoftwareStatementValidator
{
    /// <summary>
    ///     Returns <see langword="true" /> when the software statement's issuer is trusted and its
    ///     signature verifies.
    /// </summary>
    Task<bool> ValidateAsync(string softwareStatement, CancellationToken ct = default);
}
