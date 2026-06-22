using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Internal;
using WellKnownReflection = Google.Protobuf.WellKnownTypes;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Builds protobuf reflection descriptors for generated resource gRPC services.
/// </summary>
internal static class FileDescriptorBridge
{
    /// <summary>
    ///     Builds service descriptors for the supplied closed resource service types.
    /// </summary>
    /// <param name="model">The protobuf-net model for inspecting serializable members.</param>
    /// <param name="serviceTypes">Closed <see cref="IResourceService{TEntity,TRequest,TDetail,TSummary}" /> service types.</param>
    /// <param name="options">Registered resource and custom-method metadata.</param>
    /// <returns>The generated protobuf service descriptors.</returns>
    public static IReadOnlyList<ServiceDescriptor> BuildServiceDescriptors(
        RuntimeTypeModel model,
        Type[]           serviceTypes,
        SchemataResourceOptions options
    ) {
        var results = new List<ServiceDescriptor>();

        foreach (var serviceType in serviceTypes) {
            var args       = serviceType.GetGenericArguments();
            var entityType = args[0];
            var descriptor = ResourceNameDescriptor.ForType(entityType);
            var package    = descriptor.Package ?? entityType.Namespace;

            options.Methods.TryGetValue(entityType.TypeHandle, out var methods);
            options.Resources.TryGetValue(entityType.TypeHandle, out var resource);
            var file = BuildFileDescriptor(model, descriptor, package, args, methods ?? [], resource?.Operations);
            results.AddRange(file.Services);
        }

        return results;
    }

    private static FileDescriptor BuildFileDescriptor(
        RuntimeTypeModel       model,
        ResourceNameDescriptor descriptor,
        string?                package,
        Type[]                 entityArgs,
        IReadOnlyList<ResourceMethodAttribute> methods,
        Operations[]?          operations
    ) {
        var requestType    = entityArgs[1];
        var detailType     = entityArgs[2];
        var summaryType    = entityArgs[3];
        var listResultType = typeof(ListResultBase<>).MakeGenericType(summaryType);

        var proto = new FileDescriptorProto {
            Name = $"{descriptor.Singular.ToLowerInvariant()}_service.proto", Syntax = "proto3",
        };
        if (package is not null) {
            proto.Package = package;
        }

        proto.Dependency.Add("google/protobuf/empty.proto");
        proto.Dependency.Add("google/protobuf/timestamp.proto");

        var messages = new Dictionary<Type, string> {
            [typeof(ListRequest)]   = nameof(ListRequest),
            [typeof(GetRequest)]    = nameof(GetRequest),
            [typeof(DeleteRequest)] = nameof(DeleteRequest),
        };
        messages.TryAdd(requestType, requestType.Name);
        messages.TryAdd(detailType, detailType.Name);
        messages.TryAdd(summaryType, summaryType.Name);
        messages[listResultType] = $"List{descriptor.Plural}Response";

        foreach (var method in methods) {
            var iface = GrpcResourceHelper.FindHandlerInterface(method.Handler);
            if (iface is null) {
                continue;
            }

            var arguments = iface.GetGenericArguments();
            messages.TryAdd(arguments[1], arguments[1].Name);
            messages.TryAdd(arguments[2], arguments[2].Name);
        }

        foreach (var (type, name) in messages) {
            proto.MessageType.Add(BuildMessage(model, type, name, messages, package));
        }

        var fqPrefix    = package is not null ? $".{package}." : ".";
        var serviceName = GrpcResourceNaming.ServiceName(descriptor);
        var service     = new ServiceDescriptorProto { Name = serviceName };

        // Advertise only the standard verbs the transport actually serves; a null whitelist exposes
        // all five so reflection stays in step with the resource's Operations restriction.
        var allowed = operations is null ? null : new HashSet<Operations>(operations);

        if (allowed is null || allowed.Contains(Operations.List)) {
            service.Method.Add(new MethodDescriptorProto {
                Name       = GrpcResourceNaming.MethodName(descriptor, Operations.List),
                InputType  = $"{fqPrefix}{nameof(ListRequest)}",
                OutputType = $"{fqPrefix}List{descriptor.Plural}Response",
            });
        }

        if (allowed is null || allowed.Contains(Operations.Get)) {
            service.Method.Add(new MethodDescriptorProto {
                Name       = GrpcResourceNaming.MethodName(descriptor, Operations.Get),
                InputType  = $"{fqPrefix}{nameof(GetRequest)}",
                OutputType = $"{fqPrefix}{messages[detailType]}",
            });
        }

        if (allowed is null || allowed.Contains(Operations.Create)) {
            service.Method.Add(new MethodDescriptorProto {
                Name       = GrpcResourceNaming.MethodName(descriptor, Operations.Create),
                InputType  = $"{fqPrefix}{messages[requestType]}",
                OutputType = $"{fqPrefix}{messages[detailType]}",
            });
        }

        if (allowed is null || allowed.Contains(Operations.Update)) {
            service.Method.Add(new MethodDescriptorProto {
                Name       = GrpcResourceNaming.MethodName(descriptor, Operations.Update),
                InputType  = $"{fqPrefix}{messages[requestType]}",
                OutputType = $"{fqPrefix}{messages[detailType]}",
            });
        }

        if (allowed is null || allowed.Contains(Operations.Delete)) {
            // Soft-deletable resources respond with the updated resource per AIP-164;
            // hard-deletable resources respond with Empty per AIP-135.
            service.Method.Add(new MethodDescriptorProto {
                Name      = GrpcResourceNaming.MethodName(descriptor, Operations.Delete),
                InputType = $"{fqPrefix}{nameof(DeleteRequest)}",
                OutputType = typeof(ISoftDelete).IsAssignableFrom(entityArgs[0])
                    ? $"{fqPrefix}{messages[detailType]}"
                    : ".google.protobuf.Empty",
            });
        }

        foreach (var method in methods) {
            var iface = GrpcResourceHelper.FindHandlerInterface(method.Handler);
            if (iface is null) {
                continue;
            }

            var arguments = iface.GetGenericArguments();
            service.Method.Add(new MethodDescriptorProto {
                Name       = GrpcResourceNaming.CustomMethodName(descriptor, method.Verb),
                InputType  = $"{fqPrefix}{messages[arguments[1]]}",
                OutputType = $"{fqPrefix}{messages[arguments[2]]}",
            });
        }

        proto.Service.Add(service);

        var deps = new List<FileDescriptor> {
            WellKnownReflection.EmptyReflection.Descriptor,
            WellKnownReflection.TimestampReflection.Descriptor,
        };

        return FileDescriptor.BuildFromByteStrings(deps.Select(d => d.SerializedData).Append(proto.ToByteString()))
                             .Last();
    }

    private static DescriptorProto BuildMessage(
        RuntimeTypeModel         model,
        Type                     type,
        string                   name,
        Dictionary<Type, string> messages,
        string?                  package
    ) {
        var message = new DescriptorProto { Name = name };

        if (!model.CanSerialize(type)) {
            return message;
        }

        var meta = model[type];
        foreach (var field in meta.GetFields()) {
            var memberType  = field.MemberType;
            var unwrapped   = Nullable.GetUnderlyingType(memberType) ?? memberType;
            var repeated    = IsCollection(memberType);
            var elementType = repeated ? GetElementType(memberType) : unwrapped;

            var fdp = new FieldDescriptorProto {
                Name   = field.Name,
                Number = field.FieldNumber,
                Label = repeated
                    ? FieldDescriptorProto.Types.Label.Repeated
                    : FieldDescriptorProto.Types.Label.Optional,
            };

            if (messages.TryGetValue(elementType, out var typeName)) {
                fdp.Type     = FieldDescriptorProto.Types.Type.Message;
                fdp.TypeName = package is not null ? $".{package}.{typeName}" : $".{typeName}";
            } else if (elementType == typeof(DateTime)) {
                fdp.Type     = FieldDescriptorProto.Types.Type.Message;
                fdp.TypeName = ".google.protobuf.Timestamp";
            } else {
                fdp.Type = MapProtoType(elementType);
            }

            message.Field.Add(fdp);
        }

        return message;
    }

    private static FieldDescriptorProto.Types.Type MapProtoType(Type clr) {
        clr = Nullable.GetUnderlyingType(clr) ?? clr;

        if (clr == typeof(string)) return FieldDescriptorProto.Types.Type.String;
        if (clr == typeof(bool)) return FieldDescriptorProto.Types.Type.Bool;
        if (clr == typeof(int)) return FieldDescriptorProto.Types.Type.Int32;
        if (clr == typeof(long)) return FieldDescriptorProto.Types.Type.Int64;
        if (clr == typeof(uint)) return FieldDescriptorProto.Types.Type.Uint32;
        if (clr == typeof(ulong)) return FieldDescriptorProto.Types.Type.Uint64;
        if (clr == typeof(float)) return FieldDescriptorProto.Types.Type.Float;
        if (clr == typeof(double)) return FieldDescriptorProto.Types.Type.Double;
        if (clr == typeof(byte[])) return FieldDescriptorProto.Types.Type.Bytes;
        if (clr == typeof(Guid)) return FieldDescriptorProto.Types.Type.String;

        if (clr.IsEnum) return FieldDescriptorProto.Types.Type.Int32;

        return FieldDescriptorProto.Types.Type.String;
    }

    private static bool IsCollection(Type type) {
        if (type.IsArray) return true;
        if (!type.IsGenericType) return false;
        var def = type.GetGenericTypeDefinition();
        return def == typeof(IEnumerable<>)
            || def == typeof(IList<>)
            || def == typeof(ICollection<>)
            || def == typeof(List<>)
            || def == typeof(IReadOnlyList<>)
            || def == typeof(IReadOnlyCollection<>);
    }

    private static Type GetElementType(Type collectionType) {
        if (collectionType.IsArray) return collectionType.GetElementType()!;
        if (collectionType.IsGenericType) return collectionType.GetGenericArguments()[0];
        return typeof(object);
    }
}
