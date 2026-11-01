namespace Schemata.Abstractions.Resource;

/// <summary>
///     Carries a unique client-supplied request identifier for idempotency, per
///     <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
///     Implement on mutating request messages (standard and custom methods), never on the
///     resource itself. On the wire the property is <c>request_id</c> on both HTTP (snake_case
///     JSON) and gRPC (proto field); the framework-generated proto descriptor annotates the
///     field <c>(google.api.field_info).format = UUID4</c> per
///     <seealso href="https://google.aip.dev/202">AIP-202</seealso>.
/// </summary>
public interface IRequestIdentification
{
    /// <summary>
    ///     The unique, client-assigned request ID for deduplication. Restricted to 36 ASCII
    ///     characters; a random UUID (UUID4) is recommended. Providing a nonempty value
    ///     guarantees idempotency: an identical replay returns the previously successful
    ///     response within the configured retention window, and a replay carrying a different
    ///     payload fails validation on this field.
    /// </summary>
    string? RequestId { get; set; }
}
