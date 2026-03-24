using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when the caller does not have permission to perform the operation (HTTP 403).
/// </summary>
public class AuthorizationException : SchemataException
{
    /// <inheritdoc />
    public AuthorizationException(
        int     status  = 403,
        string? code    = ErrorCodes.PermissionDenied,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1005)) { }
}
