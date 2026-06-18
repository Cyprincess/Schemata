using System;
using System.Reflection;
using ProtoBuf.Grpc.Configuration;
using Schemata.Common;
using Schemata.Resource.Grpc.Internal;

namespace Schemata.Resource.Grpc;

internal sealed class ResourceServiceBinder : ServiceBinder
{
    private static readonly Type OpenServiceInterface      = typeof(IResourceService<,,,>);

    public override bool IsServiceContract(Type contractType, out string? name) {
        if (!contractType.IsConstructedGenericType || !IsResourceContract(contractType.GetGenericTypeDefinition())) {
            return base.IsServiceContract(contractType, out name);
        }

        var entityType = contractType.GetGenericArguments()[0];
        var descriptor = ResourceNameDescriptor.ForType(entityType);
        name = GrpcResourceNaming.ServiceFullName(entityType, descriptor);
        return true;
    }

    public override bool IsOperationContract(MethodInfo method, out string? name) {
        var declaringType = method.DeclaringType;
        if (declaringType?.IsConstructedGenericType != true
         || !IsResourceContract(declaringType.GetGenericTypeDefinition())) {
            return base.IsOperationContract(method, out name);
        }

        if (!base.IsOperationContract(method, out var _)) {
            name = null;
            return false;
        }

        var entityType = declaringType.GetGenericArguments()[0];
        var descriptor = ResourceNameDescriptor.ForType(entityType);
        name = GrpcResourceNaming.MethodName(descriptor, method);
        return true;
    }

    private static bool IsResourceContract(Type genericType) {
        return genericType == OpenServiceInterface;
    }
}
