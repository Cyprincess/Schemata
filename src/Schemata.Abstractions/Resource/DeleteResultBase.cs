namespace Schemata.Abstractions.Resource;

/// <summary>
///     Result of a delete operation per
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>.
///     A soft delete per <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>
///     carries the updated resource in <see cref="Detail" />; a hard delete carries nothing.
/// </summary>
/// <typeparam name="TDetail">The resource detail type.</typeparam>
public class DeleteResultBase<TDetail>
{
    /// <summary>
    ///     The soft-deleted resource detail, or <see langword="null" /> for a hard delete.
    /// </summary>
    public virtual TDetail? Detail { get; set; }
}
