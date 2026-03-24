using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown to signal a successful operation with no response body (HTTP 204).
/// </summary>
public class NoContentException : SchemataException
{
    /// <inheritdoc />
    public NoContentException(
        int     status  = 204,
        string? code    = ErrorCodes.Ok,
        string? message = null
    ) : base(status, code, message) { }
}
