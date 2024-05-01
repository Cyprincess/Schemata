namespace Schemata.Abstractions.Exceptions;

public class TenantResolveException(
    int     status  = 400,
    string? message = "An error occurred while processing your request.") : HttpException(status, message);
