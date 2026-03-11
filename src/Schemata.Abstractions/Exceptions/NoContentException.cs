namespace Schemata.Abstractions.Exceptions;

public class NoContentException : SchemataException
{
    public NoContentException(
        int     status  = 204,
        string? code    = "OK",
        string? message = null
    ) : base(status, code, message) { }
}
