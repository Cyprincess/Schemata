namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of a Get operation containing the retrieved resource detail.
/// </summary>
/// <typeparam name="TDetail">The type of the resource detail.</typeparam>
public class GetResult<TDetail> : OperationResult<GetResult<TDetail>>
{
    /// <summary>
    ///     Gets or sets the detail of the retrieved resource.
    /// </summary>
    public virtual TDetail? Detail { get; set; }

    /// <inheritdoc />
    protected override bool IsValid() { return Detail != null; }
}
