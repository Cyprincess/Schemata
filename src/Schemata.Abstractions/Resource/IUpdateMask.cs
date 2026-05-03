namespace Schemata.Abstractions.Resource;

/// <summary>
///     Supports partial updates by specifying which fields to overwrite, per
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>
///     and <seealso href="https://google.aip.dev/161">AIP-161: Field masks</seealso>.
/// </summary>
public interface IUpdateMask
{
    /// <summary>
    ///     Comma-separated field paths denoting the fields to update.
    ///     A <c>*</c> wildcard means all fields.
    /// </summary>
    string? UpdateMask { get; set; }
}
