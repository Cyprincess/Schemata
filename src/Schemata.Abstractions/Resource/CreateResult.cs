namespace Schemata.Abstractions.Resource;

public class CreateResult<TDetail> : OperationResult<CreateResult<TDetail>>
{
    public virtual TDetail? Detail { get; set; }
}
