namespace Schemata.Abstractions.Exceptions;

public class TenantResolveException : HttpException
{
    public TenantResolveException(
        int     status  = 400,
        string? message = "An error occurred while processing your request.") : base(status, message) { }
}
