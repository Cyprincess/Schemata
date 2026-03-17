namespace Schemata.Abstractions.Errors;

public class ErrorFieldViolation
{
    public virtual string? Field { get; set; }

    public virtual string? Description { get; set; }

    public virtual string? Reason { get; set; }
}
