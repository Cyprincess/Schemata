using System.Collections.Generic;
using System.Linq;

namespace Schemata.Abstractions.Exceptions;

public sealed class ValidationException : HttpException
{
    public ValidationException(
        IEnumerable<KeyValuePair<string, string>> errors,
        int code = 422,
        string? message = "An error occurred while validating the entity.") : base(code, message) {
        Errors = errors.GroupBy(kv => kv.Key)
                       .ToDictionary(g => g.Key, pairs => string.Join(",", pairs.Select(kv => kv.Value)));
    }
}
