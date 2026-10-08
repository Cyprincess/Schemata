using System;
using System.Collections.Generic;
using Google.Protobuf.Reflection;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Insight.Grpc;

internal sealed class InsightGrpcServiceDescriptorContributor : IGrpcServiceDescriptorContributor
{
    public IReadOnlyList<ServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider) =>
        GrpcSchema.Build(InsightGrpcMethods.Model, InsightGrpcMethods.Methods);
}
