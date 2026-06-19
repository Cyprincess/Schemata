using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Holds the runtime protobuf model and binder used by resource gRPC services.
/// </summary>
internal sealed class ResourceBinderConfiguration
{
    /// <summary>
    ///     Initializes a new configuration holder.
    /// </summary>
    /// <param name="model">The protobuf-net runtime model.</param>
    /// <param name="binder">The protobuf-net gRPC binder configuration.</param>
    public ResourceBinderConfiguration(RuntimeTypeModel model, BinderConfiguration binder) {
        Model  = model;
        Binder = binder;
    }

    /// <summary>
    ///     Gets the protobuf-net runtime model.
    /// </summary>
    public RuntimeTypeModel    Model  { get; }

    /// <summary>
    ///     Gets the protobuf-net gRPC binder configuration.
    /// </summary>
    public BinderConfiguration Binder { get; }
}
