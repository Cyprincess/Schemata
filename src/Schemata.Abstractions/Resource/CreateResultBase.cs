namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of a create operation per
///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>,
///     carrying the created resource detail.
/// </summary>
/// <typeparam name="TDetail">The resource detail type.</typeparam>
public class CreateResultBase<TDetail>
{
    /// <summary>
    ///     The created resource detail, or <see langword="null" /> when the mutation was handled
    ///     without a write and no row was created.
    /// </summary>
    public virtual TDetail? Detail { get; set; }
}
