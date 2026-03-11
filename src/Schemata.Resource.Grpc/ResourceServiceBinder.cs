using System;
using System.Reflection;
using Humanizer;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Options;

namespace Schemata.Resource.Grpc;

internal sealed class ResourceServiceBinder : ServiceBinder
{
    private static readonly Type OpenServiceInterface = typeof(IResourceService<,,,>);

    private readonly SchemataResourceOptions _options;

    public ResourceServiceBinder(SchemataResourceOptions options) { _options = options; }

    public override bool IsServiceContract(Type contractType, out string? name) {
        if (!contractType.IsConstructedGenericType || contractType.GetGenericTypeDefinition() != OpenServiceInterface) {
            return base.IsServiceContract(contractType, out name);
        }

        var entityType = contractType.GetGenericArguments()[0];

        string? package = null;
        if (_options.Resources.TryGetValue(entityType.TypeHandle, out var resource)) {
            package = resource.Package;
        }

        package ??= entityType.Namespace;

        name = package is not null ? $"{package}.{entityType.Name}Service" : $"{entityType.Name}Service";
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
        var baseName   = method.Name.EndsWith("Async") ? method.Name[..^5] : method.Name;

        name = baseName switch {
            "List" => $"List{entityType.Name.Pluralize()}",
            var _  => $"{baseName}{entityType.Name}",
        };
        return true;
    }
}
