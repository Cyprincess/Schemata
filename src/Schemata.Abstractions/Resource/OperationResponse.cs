using System.Text.Json.Serialization;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Successful result payload of an <see cref="Operation" /> (the AIP-151
///     <c>response</c> field).
/// </summary>
public sealed class OperationResponse
{
    /// <summary>
    ///     The serialized result document. It already holds JSON, so
    ///     <see cref="RawJsonConverter" /> emits structured JSON on the HTTP
    ///     wire; the protobuf-net (gRPC) path carries a plain string field.
    /// </summary>
    [JsonConverter(typeof(RawJsonConverter))]
    public string? Output { get; set; }
}
