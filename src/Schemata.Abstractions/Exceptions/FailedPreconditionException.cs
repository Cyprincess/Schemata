using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class FailedPreconditionException : SchemataException
{
    public FailedPreconditionException(
        int     status  = 412,
        string? code    = ErrorCodes.FailedPrecondition,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1003)) { }
}
