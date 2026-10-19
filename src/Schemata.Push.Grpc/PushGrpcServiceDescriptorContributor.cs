using System;
using System.Collections.Generic;
using Google.Protobuf.Reflection;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Push.Grpc;

internal sealed class PushGrpcServiceDescriptorContributor(PushGrpcModel model) : IGrpcServiceDescriptorContributor
{
    public IReadOnlyList<ServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider) =>
        GrpcSchema.Build(model.Model, model.Methods);
}
