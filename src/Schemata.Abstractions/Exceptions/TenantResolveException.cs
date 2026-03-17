using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class TenantResolveException : SchemataException
{
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
