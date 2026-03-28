using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using WellKnownReflection = Google.Protobuf.WellKnownTypes;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Builds <see cref="ServiceDescriptor" /> objects for grpc-dotnet's <c>ReflectionServiceImpl</c>
///     by reading field metadata directly from <see cref="RuntimeTypeModel" />.
/// </summary>
internal static class FileDescriptorBridge
{
    public static IReadOnlyList<ServiceDescriptor> BuildServiceDescriptors(
        RuntimeTypeModel model,
        Type[]           serviceTypes
    ) {
        var results = new List<ServiceDescriptor>();

        foreach (var serviceType in serviceTypes) {
            var args       = serviceType.GetGenericArguments();
            var entityType = args[0];
            var descriptor = ResourceNameDescriptor.ForType(entityType);
            var package    = descriptor.Package ?? entityType.Namespace;

            var file = BuildFileDescriptor(model, descriptor, package, args);
            results.AddRange(file.Services);
        }

        return results;
    }

    private static FileDescriptor BuildFileDescriptor(
        RuntimeTypeModel       model,
        ResourceNameDescriptor descriptor,
        string?                package,
        Type[]                 entityArgs
    ) {
        var requestType    = entityArgs[1];
        var detailType     = entityArgs[2];
        var summaryType    = entityArgs[3];
        var listResultType = typeof(ListResult<>).MakeGenericType(summaryType);

        var proto = new FileDescriptorProto {
            Name = $"{descriptor.Singular.ToLowerInvariant()}_service.proto", Syntax = "proto3",
        };
        if (package is not null) {
            proto.Package = package;
        }

        proto.Dependency.Add("google/protobuf/empty.proto");

        // Collect message types (deduplicate by CLR type)
        var messages = new Dictionary<Type, string> {
            [typeof(ListRequest)]   = nameof(ListRequest),
            [typeof(GetRequest)]    = nameof(GetRequest),
            [typeof(DeleteRequest)] = nameof(DeleteRequest),
        };
        messages.TryAdd(requestType, requestType.Name);
        messages.TryAdd(detailType, detailType.Name);
        messages.TryAdd(summaryType, summaryType.Name);
        messages[listResultType] = $"List{descriptor.Plural}Response";

        // Build and add message descriptors
        // Only scalar and well-known types are supported; nested message types
        // would require recursive descriptor building — add when needed.
        foreach (var (type, name) in messages) {
            proto.MessageType.Add(BuildMessage(model, type, name));
        }

        // Build service descriptor
        var fqPrefix    = package is not null ? $".{package}." : ".";
        var serviceName = $"{descriptor.Singular}Service";
        var service     = new ServiceDescriptorProto { Name = serviceName };

        service.Method.Add(new MethodDescriptorProto {
            Name       = $"List{descriptor.Plural}",
            InputType  = $"{fqPrefix}{nameof(ListRequest)}",
            OutputType = $"{fqPrefix}List{descriptor.Plural}Response",
        });
        service.Method.Add(new MethodDescriptorProto {
            Name       = $"Get{descriptor.Singular}",
            InputType  = $"{fqPrefix}{nameof(GetRequest)}",
            OutputType = $"{fqPrefix}{messages[detailType]}",
        });
        service.Method.Add(new MethodDescriptorProto {
            Name       = $"Create{descriptor.Singular}",
            InputType  = $"{fqPrefix}{messages[requestType]}",
            OutputType = $"{fqPrefix}{messages[detailType]}",
        });
        service.Method.Add(new MethodDescriptorProto {
            Name       = $"Update{descriptor.Singular}",
            InputType  = $"{fqPrefix}{messages[requestType]}",
            OutputType = $"{fqPrefix}{messages[detailType]}",
        });
        service.Method.Add(new MethodDescriptorProto {
            Name       = $"Delete{descriptor.Singular}",
            InputType  = $"{fqPrefix}{nameof(DeleteRequest)}",
            OutputType = ".google.protobuf.Empty",
        });
        proto.Service.Add(service);

        // Serialize and build FileDescriptor with well-known dependencies
        var deps = new[] { WellKnownReflection.EmptyReflection.Descriptor };

        return FileDescriptor.BuildFromByteStrings(deps.Select(d => d.SerializedData).Append(proto.ToByteString()))
                             .Last();
    }

    private static DescriptorProto BuildMessage(RuntimeTypeModel model, Type type, string name) {
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
                Type   = MapProtoType(elementType),
                Label = repeated
                    ? FieldDescriptorProto.Types.Label.Repeated
                    : FieldDescriptorProto.Types.Label.Optional,
            };

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

        // Enums use int32 wire encoding in proto3; avoids needing EnumDescriptorProto.
        if (clr.IsEnum) return FieldDescriptorProto.Types.Type.Int32;

        // Unknown complex types: string is a safe fallback for reflection display.
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
