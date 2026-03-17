using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public class AlreadyExistsException : SchemataException
{
    public AlreadyExistsException(
        int     status  = 409,
        string? code    = ErrorCodes.AlreadyExists,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1007)) { }
}
