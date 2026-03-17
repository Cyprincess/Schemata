using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class NoContentException : SchemataException
{
    public NoContentException(
        int     status  = 204,
        string? code    = ErrorCodes.Ok,
        string? message = null
    ) : base(status, code, message) { }
}
