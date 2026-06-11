namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of a get operation per
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>,
///     carrying the full detail of a single resource.
/// </summary>
/// <typeparam name="TDetail">The resource detail type.</typeparam>
public class GetResultBase<TDetail>
{
    /// <summary>
    ///     The retrieved resource detail.
    /// </summary>
    public virtual TDetail? Detail { get; set; }
}
