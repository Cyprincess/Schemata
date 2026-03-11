namespace Schemata.Abstractions.Exceptions;

public sealed class ConcurrencyException : SchemataException
{
    public ConcurrencyException(
        int     status  = 409,
        string? code    = "ABORTED",
        string? message = "A concurrency violation is encountered while saving to the database."
    ) : base(status, code, message) {
        Error = "concurrency_mismatch";
    }
}
