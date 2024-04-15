namespace Schemata.Abstractions.Resource;

public class ListRequest
{
    public virtual string? Filter { get; set; }

    public virtual string? OrderBy { get; set; }

    public virtual bool? ShowDeleted { get; set; }

    public virtual int? PageSize { get; set; }

    public virtual int? Skip { get; set; }

    public virtual string? PageToken { get; set; }
}
