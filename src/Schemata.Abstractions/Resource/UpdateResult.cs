namespace Schemata.Abstractions.Resource;

public class UpdateResult<TDetail> : OperationResult<UpdateResult<TDetail>>
{
    public virtual TDetail? Detail { get; set; }
}
