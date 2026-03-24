using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when a rate limit or quota has been exceeded (HTTP 429).
/// </summary>
public class QuotaExceededException : SchemataException
{
    /// <inheritdoc />
    public QuotaExceededException(
        int     status  = 429,
        string? code    = ErrorCodes.ResourceExhausted,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1011)) { }
}
