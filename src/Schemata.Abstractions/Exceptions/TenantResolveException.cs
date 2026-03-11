namespace Schemata.Abstractions.Exceptions;

public class TenantResolveException : SchemataException
{
    public TenantResolveException(
        int     status  = 400,
        string? code    = "INVALID_ARGUMENT",
        string? message = "An error occurred while processing your request."
    ) : base(status, code, message) { }
}
