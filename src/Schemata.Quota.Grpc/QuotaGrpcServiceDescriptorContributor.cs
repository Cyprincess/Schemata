using System;
using System.Collections.Generic;
using Google.Protobuf.Reflection;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Quota.Grpc;

internal sealed class QuotaGrpcServiceDescriptorContributor(QuotaGrpcModel model) : IGrpcServiceDescriptorContributor
{
    public IReadOnlyList<ServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider) =>
        GrpcSchema.Build(model.Model, model.Methods);
}
