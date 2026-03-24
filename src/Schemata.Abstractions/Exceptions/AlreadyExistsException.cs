using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when a resource already exists and cannot be created again (HTTP 409).
/// </summary>
public class AlreadyExistsException : SchemataException
{
    /// <inheritdoc />
    public AlreadyExistsException(
        int     status  = 409,
        string? code    = ErrorCodes.AlreadyExists,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1007)) { }
}
