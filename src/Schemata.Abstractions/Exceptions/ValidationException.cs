using System.Collections.Generic;
using System.Linq;

namespace Schemata.Abstractions.Exceptions;

public sealed class ValidationException : SchemataException
{
    public ValidationException(
        IEnumerable<KeyValuePair<string, string>> errors,
        int                                       status  = 422,
        string?                                   code    = "INVALID_ARGUMENT",
        string?                                   message = "An error occurred while validating the entity."
    ) : base(status, code, message) {
        Errors = errors.GroupBy(kv => kv.Key)
                       .ToDictionary(g => g.Key, pairs => string.Join(",", pairs.Select(kv => kv.Value)));
    }
}
