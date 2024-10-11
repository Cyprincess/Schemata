namespace Schemata.Abstractions.Exceptions;

public class InvalidArgumentException : HttpException
{
    public InvalidArgumentException(
        int     status  = 400,
        string? message = "An error occurred while processing your request.") : base(status, message) { }
}
