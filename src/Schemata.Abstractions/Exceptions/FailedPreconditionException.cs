using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when a precondition for the operation is not met (HTTP 412).
/// </summary>
public class FailedPreconditionException : SchemataException
{
    /// <inheritdoc />
    public FailedPreconditionException(
        int     status  = 412,
        string? code    = ErrorCodes.FailedPrecondition,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1003)) { }
}
