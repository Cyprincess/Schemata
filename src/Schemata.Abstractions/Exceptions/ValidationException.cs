using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Exceptions;

public sealed class ValidationException : Exception
{
    public ValidationException(List<KeyValuePair<string, string>> errors) {
        Errors = errors;
    }

    public List<KeyValuePair<string, string>> Errors { get; }
}
