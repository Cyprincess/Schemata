namespace Schemata.Abstractions.Exceptions;

public class NotFoundException : SchemataException
{
    public NotFoundException(
        int     status  = 404,
        string? code    = "NOT_FOUND",
        string? message = "Requested resource not found."
    ) : base(status, code, message) { }
}
