using System.Collections.Generic;

namespace Schemata.Abstractions.Exceptions;

public class ErrorResponse
{
    public virtual string? Error { get; set; }

    public virtual Dictionary<string, string>? Errors { get; set; }

    public virtual string? ErrorDescription { get; set; }
}
