using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when a requested resource cannot be found (HTTP 404).
/// </summary>
public class NotFoundException : SchemataException
{
    /// <inheritdoc />
    public NotFoundException(
        int     status  = 404,
        string? code    = ErrorCodes.NotFound,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1006)) { }
}
