namespace Schemata.Abstractions.Exceptions;

public class NoContentException : HttpException
{
    public NoContentException(int code = 204, string? message = null) : base(code, message) { }
}
