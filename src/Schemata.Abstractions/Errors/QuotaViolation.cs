namespace Schemata.Abstractions.Errors;

public class QuotaViolation
{
    public virtual string? Subject { get; set; }

    public virtual string? Description { get; set; }
}
