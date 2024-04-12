namespace Schemata.Abstractions.Exceptions;

public class AuthorizationException : HttpException
{
    public AuthorizationException(int status = 401, string? message = null) : base(status, message) { }
}
