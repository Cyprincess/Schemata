namespace Schemata.Abstractions;

public class ErrorResponse
{
    public virtual string? Error { get; set; }

    public virtual string? ErrorDescription { get; set; }
}
