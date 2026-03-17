using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class AuthorizationException : SchemataException
{
    public AuthorizationException(
        int     status  = 403,
        string? code    = ErrorCodes.PermissionDenied,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1005)) { }
}
