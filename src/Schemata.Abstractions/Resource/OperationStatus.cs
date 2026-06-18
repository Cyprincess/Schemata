namespace Schemata.Abstractions.Resource;

/// <summary>
///     Terminal error status of an <see cref="Operation" />, pairing an integer
///     <c>google.rpc.Code</c> with a developer-facing message per
///     <seealso href="https://google.aip.dev/151">AIP-151</seealso>.
/// </summary>
public sealed class OperationStatus
{
    /// <summary>
    ///     Integer <c>google.rpc.Code</c> for the failure
    ///     (e.g. <c>1</c> = CANCELLED, <c>2</c> = UNKNOWN).
    /// </summary>
    public int Code { get; set; }

    /// <summary>Developer-oriented diagnostic message.</summary>
    public string? Message { get; set; }
}
