using System;
using System.Reflection;
using ProtoBuf.Grpc.Configuration;
using Schemata.Common;

namespace Schemata.Resource.Grpc;

internal sealed class ResourceServiceBinder : ServiceBinder
{
    private static readonly Type OpenServiceInterface = typeof(IResourceService<,,,>);

    public override bool IsServiceContract(Type contractType, out string? name) {
        if (!contractType.IsConstructedGenericType || contractType.GetGenericTypeDefinition() != OpenServiceInterface) {
            return base.IsServiceContract(contractType, out name);
        }

        var entityType = contractType.GetGenericArguments()[0];
        var descriptor = ResourceNameDescriptor.ForType(entityType);
        var package    = descriptor.Package ?? entityType.Namespace;

        name = package is not null ? $"{package}.{descriptor.Singular}Service" : $"{descriptor.Singular}Service";
        return true;
    }

    public override bool IsOperationContract(MethodInfo method, out string? name) {
        var declaringType = method.DeclaringType;
        if (declaringType?.IsConstructedGenericType != true
         || declaringType.GetGenericTypeDefinition() != OpenServiceInterface) {
            return base.IsOperationContract(method, out name);
        }

        if (!base.IsOperationContract(method, out var _)) {
            name = null;
            return false;
        }

        var entityType = declaringType.GetGenericArguments()[0];
        var descriptor = ResourceNameDescriptor.ForType(entityType);
        var baseName   = method.Name.EndsWith("Async") ? method.Name[..^5] : method.Name;

        name = baseName switch {
            "List" => $"List{descriptor.Plural}",
            var _  => $"{baseName}{descriptor.Singular}",
        };
        return true;
    }
}
