namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of an update operation per
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>,
///     carrying the updated resource detail.
/// </summary>
/// <typeparam name="TDetail">The resource detail type.</typeparam>
public class UpdateResultBase<TDetail> : OperationResultBase<UpdateResultBase<TDetail>>
{
    /// <summary>
    ///     The updated resource detail, or <see langword="null" /> for async operations.
    /// </summary>
    public virtual TDetail? Detail { get; set; }
}
