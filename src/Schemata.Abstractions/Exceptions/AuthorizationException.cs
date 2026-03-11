namespace Schemata.Abstractions.Exceptions;

public class AuthorizationException : SchemataException
{
    public AuthorizationException(
        int     status  = 403,
        string? code    = "PERMISSION_DENIED",
        string? message = "You do not have permission to perform this action."
    ) : base(status, code, message) { }
}
