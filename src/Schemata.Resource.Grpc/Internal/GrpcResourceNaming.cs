using System;
using System.Reflection;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Resource.Grpc.Internal;

/// <summary>
///     Resolves resource service and RPC names for generated gRPC endpoints.
/// </summary>
public static class GrpcResourceNaming
{
    /// <summary>
    ///     Gets the fully qualified service name for a resource entity type.
    /// </summary>
    /// <param name="entityType">The resource entity type.</param>
    /// <returns>The package-qualified service name.</returns>
    public static string ServiceFullName(Type entityType) {
        var descriptor = ResourceNameDescriptor.ForType(entityType);
        return ServiceFullName(entityType, descriptor);
    }

    /// <summary>
    ///     Gets the fully qualified service name for a resource descriptor.
    /// </summary>
    /// <param name="entityType">The resource entity type.</param>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    /// <returns>The package-qualified service name.</returns>
    public static string ServiceFullName(Type entityType, ResourceNameDescriptor descriptor) {
        var package = descriptor.Package ?? entityType.Namespace;
        return package is not null ? $"{package}.{descriptor.Singular}Service" : $"{descriptor.Singular}Service";
    }

    /// <summary>
    ///     Gets the unqualified service name for a resource descriptor.
    /// </summary>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    /// <returns>The service name.</returns>
    public static string ServiceName(ResourceNameDescriptor descriptor) {
        return $"{descriptor.Singular}Service";
    }

    /// <summary>
    ///     Gets the RPC name for a standard resource operation.
    /// </summary>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    /// <param name="operation">The standard resource operation.</param>
    /// <returns>The RPC method name.</returns>
    public static string MethodName(ResourceNameDescriptor descriptor, Operations operation) {
        return operation == Operations.List ? $"List{descriptor.Plural}" : $"{operation}{descriptor.Singular}";
    }

    /// <summary>
    ///     Gets the RPC name for a resource service method.
    /// </summary>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    /// <param name="method">The service method.</param>
    /// <returns>The RPC method name.</returns>
    public static string MethodName(ResourceNameDescriptor descriptor, MethodInfo method) {
        var baseName = method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^5] : method.Name;
        return baseName == nameof(Operations.List) ? $"List{descriptor.Plural}" : $"{baseName}{descriptor.Singular}";
    }

    /// <summary>
    ///     Gets the RPC name for a custom resource method verb.
    /// </summary>
    /// <param name="descriptor">The resolved resource name descriptor.</param>
    /// <param name="verb">The AIP-136 custom method verb.</param>
    /// <returns>The custom RPC method name.</returns>
    public static string CustomMethodName(ResourceNameDescriptor descriptor, string verb) {
        return ResourceMethodNaming.GetRpcName(verb, descriptor.Singular);
    }
}
