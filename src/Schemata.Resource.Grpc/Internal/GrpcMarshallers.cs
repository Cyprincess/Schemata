using Grpc.Core;
using ProtoBuf.Meta;

namespace Schemata.Resource.Grpc.Internal;

internal static class GrpcMarshallers
{
    public static Marshaller<T> Create<T>(RuntimeTypeModel model) {
        return new((value, ctx) => {
            model.Serialize(ctx.GetBufferWriter(), value);
            ctx.Complete();
        }, ctx => model.Deserialize<T>(ctx.PayloadAsReadOnlySequence()));
    }
}
