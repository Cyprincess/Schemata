namespace Schemata.Abstractions.Exceptions;

public class AuthorizationException : HttpException
{
    public AuthorizationException(int status = 403, string? message = "") : base(status, message) { }
}
