namespace Schemata.Abstractions.Exceptions;

public sealed class ConcurrencyException : HttpException
{
    public ConcurrencyException(
        int     status  = 409,
        string? message = "A concurrency violation is encountered while saving to the database.") : base(status,
        message) {
        Error = "concurrency_mismatch";
    }
}
