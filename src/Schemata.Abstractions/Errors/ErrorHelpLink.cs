namespace Schemata.Abstractions.Errors;

/// <summary>
///     One link to supplemental documentation carried inside <see cref="HelpDetail" />,
///     per <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
public class ErrorHelpLink
{
    /// <summary>
    ///     Plain-text description suitable for display as the hyperlink text.
    /// </summary>
    public virtual string? Description { get; set; }

    /// <summary>
    ///     Absolute URL of the linked documentation, including scheme.
    /// </summary>
    public virtual string? Url { get; set; }
}
