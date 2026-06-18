using System;
using System.Reflection;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Resource.Grpc.Internal;

public static class GrpcResourceNaming
{
    public static string ServiceFullName(Type entityType) {
        var descriptor = ResourceNameDescriptor.ForType(entityType);
        return ServiceFullName(entityType, descriptor);
    }

    public static string ServiceFullName(Type entityType, ResourceNameDescriptor descriptor) {
        var package = descriptor.Package ?? entityType.Namespace;
        return package is not null ? $"{package}.{descriptor.Singular}Service" : $"{descriptor.Singular}Service";
    }

    public static string ServiceName(ResourceNameDescriptor descriptor) {
        return $"{descriptor.Singular}Service";
    }

    public static string MethodName(ResourceNameDescriptor descriptor, Operations operation) {
        return operation == Operations.List ? $"List{descriptor.Plural}" : $"{operation}{descriptor.Singular}";
    }

    public static string MethodName(ResourceNameDescriptor descriptor, MethodInfo method) {
        var baseName = method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^5] : method.Name;
        return baseName == nameof(Operations.List) ? $"List{descriptor.Plural}" : $"{baseName}{descriptor.Singular}";
    }

    public static string CustomMethodName(ResourceNameDescriptor descriptor, string verb) {
        return ResourceMethodNaming.GetRpcName(verb, descriptor.Singular);
    }
}
