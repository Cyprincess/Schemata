namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of a get operation per
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>,
///     carrying the full detail of a single resource. The result is valid only when
///     <see cref="Detail" /> is non-<see langword="null" />.
/// </summary>
/// <typeparam name="TDetail">The resource detail type.</typeparam>
public class GetResultBase<TDetail> : OperationResultBase<GetResultBase<TDetail>>
{
    /// <summary>
    ///     The retrieved resource detail, or <see langword="null" /> if not found.
    /// </summary>
    public virtual TDetail? Detail { get; set; }

    protected override bool IsValid() { return Detail != null; }
}
