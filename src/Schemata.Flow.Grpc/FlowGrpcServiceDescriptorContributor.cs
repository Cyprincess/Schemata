using System;
using System.Collections.Generic;
using Google.Protobuf.Reflection;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Flow.Grpc;

internal sealed class FlowGrpcServiceDescriptorContributor : IGrpcServiceDescriptorContributor
{
    public IReadOnlyList<ServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider) =>
        GrpcSchema.Build(FlowGrpcMethods.Model, FlowGrpcMethods.Methods);
}
