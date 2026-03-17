using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

public sealed class ValidationException : SchemataException
{
    public ValidationException(
        IEnumerable<ErrorFieldViolation> errors,
        int                              status  = 422,
        string?                          code    = ErrorCodes.InvalidArgument,
        string?                          message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1009)) {
        Details = [new BadRequestDetail { FieldViolations = errors.ToList() }];
    }
}
