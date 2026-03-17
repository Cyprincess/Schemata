using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class NotFoundException : SchemataException
{
    public NotFoundException(
        int     status  = 404,
        string? code    = ErrorCodes.NotFound,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1006)) { }
}
