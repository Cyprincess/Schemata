using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;

namespace Schemata.Resource.Grpc;

internal sealed class ResourceBinderConfiguration
{
    public ResourceBinderConfiguration(RuntimeTypeModel model, BinderConfiguration binder) {
        Model  = model;
        Binder = binder;
    }

    public RuntimeTypeModel    Model  { get; }
    public BinderConfiguration Binder { get; }
}
