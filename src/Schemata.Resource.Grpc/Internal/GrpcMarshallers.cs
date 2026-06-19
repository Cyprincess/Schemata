using Grpc.Core;
using ProtoBuf.Meta;

namespace Schemata.Resource.Grpc.Internal;

/// <summary>
///     Creates gRPC marshallers backed by a protobuf-net runtime model.
/// </summary>
internal static class GrpcMarshallers
{
    /// <summary>
    ///     Creates a marshaller for <typeparamref name="T" /> using the supplied model.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="model">The protobuf-net runtime model.</param>
    /// <returns>A marshaller that serializes and deserializes <typeparamref name="T" />.</returns>
    public static Marshaller<T> Create<T>(RuntimeTypeModel model) {
        return new((value, ctx) => {
            model.Serialize(ctx.GetBufferWriter(), value);
            ctx.Complete();
        }, ctx => model.Deserialize<T>(ctx.PayloadAsReadOnlySequence()));
    }
}
