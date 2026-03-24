namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of a Create operation containing the created resource detail.
/// </summary>
/// <typeparam name="TDetail">The type of the created resource detail.</typeparam>
public class CreateResult<TDetail> : OperationResult<CreateResult<TDetail>>
{
    /// <summary>
    ///     Gets or sets the detail of the created resource.
    /// </summary>
    public virtual TDetail? Detail { get; set; }
}
