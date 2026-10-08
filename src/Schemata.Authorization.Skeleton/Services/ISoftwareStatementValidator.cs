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
    ///     Verifies the statement's signature and validity, then applies issuer approval policy.
    ///     Invalid statements return <see cref="SoftwareStatementValidationResult.Invalid" />;
    ///     valid but unapproved statements return <see cref="SoftwareStatementValidationResult.Unapproved" />.
    ///     Approved snake_case metadata claims take precedence over the plain request body.
    /// </summary>
    Task<SoftwareStatementValidationResult> ValidateAndExtractAsync(
        string softwareStatement, CancellationToken ct = default);
}
