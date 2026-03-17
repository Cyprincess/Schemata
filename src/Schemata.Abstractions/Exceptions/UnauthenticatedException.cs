using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class UnauthenticatedException : SchemataException
{
    public UnauthenticatedException(
        int     status  = 401,
        string? code    = ErrorCodes.Unauthenticated,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1004)) { }
}
