using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The incoming request lacks a resolvable tenant.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.FAILED_PRECONDITION</c> (HTTP 400), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="ErrorReasons.TenantResolutionFailed" /> on
///     <see cref="ErrorInfoDetail" /> plus a <see cref="PreconditionFailureDetail" /> with
///     a <c>"TENANT"</c> violation entry.
/// </remarks>
public class TenantResolveException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="TenantResolveException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public TenantResolveException(
        int     code    = 400,
        string? status  = ErrorCodes.FailedPrecondition,
        string? message = null
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.TENANT_RESOLUTION_FAILED)) {
        Details = [
            new ErrorInfoDetail { Reason = ErrorReasons.TenantResolutionFailed },
            new PreconditionFailureDetail {
                Violations = [new() {
                    Type        = Keys.Tenancy,
                    Subject     = PreconditionSubjects.Request,
                    Description = SchemataResources.GetResourceString(SchemataResources.TENANT_RESOLUTION_FAILED),
                }],
            },
        ];
    }
}
