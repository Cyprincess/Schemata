namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of an Update operation containing the updated resource detail.
/// </summary>
/// <typeparam name="TDetail">The type of the updated resource detail.</typeparam>
public class UpdateResult<TDetail> : OperationResult<UpdateResult<TDetail>>
{
    /// <summary>
    ///     Gets or sets the detail of the updated resource.
    /// </summary>
    public virtual TDetail? Detail { get; set; }
}
