using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Holds the resource-specific <see cref="BinderConfiguration" /> and <see cref="RuntimeTypeModel" />
///     so they are not registered as bare singletons in DI, avoiding interference with user-registered
///     protobuf-net.Grpc services.
/// </summary>
internal sealed class ResourceBinderConfiguration
{
    public ResourceBinderConfiguration(RuntimeTypeModel model, BinderConfiguration binder) {
        Model  = model;
        Binder = binder;
    }

    public RuntimeTypeModel    Model  { get; }
    public BinderConfiguration Binder { get; }
}
