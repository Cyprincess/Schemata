namespace Schemata.Abstractions.Resource;

public class GetResult<TDetail> : OperationResult<GetResult<TDetail>>
{
    public virtual TDetail? Detail { get; set; }

    protected override bool IsValid() { return Detail != null; }
}
