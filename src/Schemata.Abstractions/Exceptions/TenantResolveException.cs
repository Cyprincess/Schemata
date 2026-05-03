using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The tenant could not be resolved from the incoming request.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.FAILED_PRECONDITION</c> (HTTP 400), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>,
///     and attaches a <see cref="PreconditionFailureDetail" /> with a <c>"TENANT"</c>
///     violation entry.
/// </remarks>
public class TenantResolveException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="TenantResolveException" />.
    /// </summary>
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public TenantResolveException(
        int     status  = 400,
        string? code    = ErrorCodes.FailedPrecondition,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1002)) {
        Details = [new PreconditionFailureDetail {
            Violations = [new() {
                Type        = Keys.Tenancy,
                Subject     = PreconditionSubjects.Request,
                Description = SchemataResources.GetResourceString(SchemataResources.ST1002),
            }],
        }];
    }
}
