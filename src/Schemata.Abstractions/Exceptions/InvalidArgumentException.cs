using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when one or more request arguments are invalid (HTTP 422).
/// </summary>
public class InvalidArgumentException : SchemataException
{
    /// <inheritdoc />
    public InvalidArgumentException(
        int     status  = 422,
        string? code    = ErrorCodes.InvalidArgument,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1001)) { }
}
