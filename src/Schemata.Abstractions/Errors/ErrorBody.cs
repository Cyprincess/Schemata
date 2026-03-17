using System.Collections.Generic;

namespace Schemata.Abstractions.Errors;

public class ErrorBody
{
    public virtual string? Code { get; set; }

    public virtual string? Message { get; set; }

    public virtual List<IErrorDetail>? Details { get; set; }
}
