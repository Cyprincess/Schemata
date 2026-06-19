using Schemata.Abstractions.Json;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Error detail carrying a localized end-user message per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Use this alongside the developer-facing <c>message</c> in <see cref="ErrorBody" />
///     when the caller requested a specific locale or the service publishes a stable
///     message shape for clients.
/// </summary>
/// <remarks>
///     Extension detail payload whose shape is defined by the framework and attached by
///     the application layer.
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
    ///     Localized message intended for end-user display.
    ///     <see cref="ErrorInfoDetail.Metadata" /> carries dynamic substitutions for
    ///     machine consumers.
    /// </summary>
    public virtual string? Message { get; set; }
}
