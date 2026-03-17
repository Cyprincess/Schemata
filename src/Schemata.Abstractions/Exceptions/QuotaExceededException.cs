using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class QuotaExceededException : SchemataException
{
    public QuotaExceededException(
        int     status  = 429,
        string? code    = ErrorCodes.ResourceExhausted,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1011)) { }
}
