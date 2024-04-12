namespace Schemata.Abstractions.Exceptions;

public class AuthorizationException : HttpException
{
    public AuthorizationException(int status = 401, string? message = "") : base(status, message) { }
}
