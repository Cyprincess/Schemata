using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when an optimistic concurrency check fails (HTTP 409, ABORTED).
/// </summary>
public sealed class ConcurrencyException : SchemataException
{
    /// <inheritdoc />
    public ConcurrencyException(
        int     status  = 409,
        string? code    = ErrorCodes.Aborted,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1008)) {
        Details = [new ErrorInfoDetail { Reason = ErrorReasons.ConcurrencyMismatch }];
    }
}
