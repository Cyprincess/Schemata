namespace Schemata.Abstractions.Errors;

public class PreconditionViolation
{
    public virtual string? Type { get; set; }

    public virtual string? Subject { get; set; }

    public virtual string? Description { get; set; }
}
