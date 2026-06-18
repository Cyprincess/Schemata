using System.Text.Json.Serialization;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Successful result payload of an <see cref="Operation" /> (the AIP-151
///     <c>response</c> field).
/// </summary>
public sealed class OperationResponse
{
    /// <summary>
    ///     The serialized result document. It already holds a JSON document, so
    ///     <see cref="RawJsonConverter" /> emits it as structured JSON on the HTTP
    ///     wire rather than an escaped string; the protobuf-net (gRPC) path carries
    ///     it as a plain string field.
    /// </summary>
    [JsonConverter(typeof(RawJsonConverter))]
    public string? Output { get; set; }
}
