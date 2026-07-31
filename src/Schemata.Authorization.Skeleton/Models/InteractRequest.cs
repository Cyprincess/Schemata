namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Parameters for querying or completing an interaction at the interaction endpoint.</summary>
public class InteractRequest
{
    /// <summary>Opaque interaction code returned in a previous redirect.</summary>
    public string? Code { get; set; }

    /// <summary>URI identifying the interaction type (e.g. device verification, consent).</summary>
    public string? CodeType { get; set; }

    /// <summary>
    ///     Device-flow user code, bound from the <c>user_code</c> parameter per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.3.1">
    ///         RFC 8628 §3.3.1
    ///     </seealso>.
    /// </summary>
    public string? UserCode { get; set; }
}
