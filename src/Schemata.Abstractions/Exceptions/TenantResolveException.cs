using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when the tenant cannot be resolved from the request (HTTP 400, FAILED_PRECONDITION).
/// </summary>
public class TenantResolveException : SchemataException
{
    /// <inheritdoc />
    public TenantResolveException(
        int     status  = 400,
        string? code    = ErrorCodes.FailedPrecondition,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1002)) {
        Details = [
            new PreconditionFailureDetail {
                Violations = [
                    new() {
                        Type        = PreconditionTypes.Tenant,
                        Subject     = PreconditionSubjects.Request,
                        Description = SchemataResources.GetResourceString(SchemataResources.ST1002),
                    },
                ],
            },
        ];
    }
}
