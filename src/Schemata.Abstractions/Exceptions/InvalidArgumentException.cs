using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class InvalidArgumentException : SchemataException
{
    public InvalidArgumentException(
        int     status  = 422,
        string? code    = ErrorCodes.InvalidArgument,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1001)) { }
}
