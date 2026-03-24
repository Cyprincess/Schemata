using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when the caller is not authenticated (HTTP 401).
/// </summary>
public class UnauthenticatedException : SchemataException
{
    /// <inheritdoc />
    public UnauthenticatedException(
        int     status  = 401,
        string? code    = ErrorCodes.Unauthenticated,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1004)) { }
}
