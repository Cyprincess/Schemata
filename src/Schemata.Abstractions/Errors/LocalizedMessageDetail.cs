using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail carrying a localized end-user message per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Use this alongside the developer-facing <c>message</c> in <see cref="ErrorBody" />
///     when the caller asked for a specific locale or when the service wants to provide a
///     consistently-shaped message that can evolve over time without breaking clients that
///     parsed <c>ErrorBody.Message</c>.
/// </summary>
/// <remarks>
///     An extension detail payload: the framework defines and serializes the shape, but the
///     application layer decides when to attach it to an error. The framework core never
///     populates it on its own.
/// </remarks>
[Polymorphic(typeof(IErrorDetail), Name = "type.googleapis.com/google.rpc.LocalizedMessage")]
public class LocalizedMessageDetail : IErrorDetail
{
    /// <summary>
    ///     IETF BCP-47 language tag identifying the locale of <see cref="Message" />
    ///     (e.g. <c>"en-US"</c>, <c>"zh-Hans"</c>, <c>"fr-CH"</c>).
    /// </summary>
    public virtual string? Locale { get; set; }

    /// <summary>
    ///     Localized message intended for end-user display. Any dynamic substitutions
    ///     must also appear under <see cref="ErrorInfoDetail.Metadata" /> so machine
    ///     consumers do not have to parse the text.
    /// </summary>
    public virtual string? Message { get; set; }
}
