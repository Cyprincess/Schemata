namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks an update request as create-capable per
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>:
///     when the addressed resource does not exist and <see cref="AllowMissing" /> is
///     <see langword="true" />, the update creates it, applying every field and ignoring the
///     update mask.
/// </summary>
public interface IAllowMissing
{
    /// <summary>
    ///     When <see langword="true" />, a missing resource is created instead of failing
    ///     with <c>NOT_FOUND</c>.
    /// </summary>
    bool AllowMissing { get; set; }
}
