extern alias ProtoReflection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Schemata.Transport.Grpc.Proto;

public sealed record GrpcMethodSchema(string Service, string Name, Type Request, Type Response, MethodType Kind)
{
    public static GrpcMethodSchema From<TRequest, TResponse>(Method<TRequest, TResponse> method) =>
        new(method.ServiceName, method.Name, typeof(TRequest), typeof(TResponse), method.Type);
}

public static class GrpcSchema
{
    // protobuf-net 3.2.56 GetSchema omits custom-serializer messages; its field emitter retains
    // the model's enum, map, compatibility-level, and well-known-type rules for those messages.
    private static readonly MethodInfo WriteSchema = typeof(MetaType).GetMethod("WriteSchema", BindingFlags.Instance | BindingFlags.NonPublic,
        null, [typeof(HashSet<Type>), typeof(StringBuilder), typeof(int), typeof(HashSet<string>), typeof(ProtoSyntax), typeof(string), typeof(SchemaGenerationFlags)], null)
        ?? throw new MissingMethodException(typeof(MetaType).FullName, "WriteSchema");

    public static IReadOnlyList<ServiceDescriptor> Build(RuntimeTypeModel model, IEnumerable<GrpcMethodSchema> methods) {
        var results = new List<ServiceDescriptor>();
        foreach (var group in methods.GroupBy(method => method.Service, StringComparer.Ordinal)) {
            var separator = group.Key.LastIndexOf('.');
            var options = new SchemaGenerationOptions {
                Syntax = ProtoSyntax.Proto2,
                Flags = SchemaGenerationFlags.IncludeEnumNamePrefix,
                Package = separator < 0 ? "" : group.Key[..separator],
            };
            var service = new Service { Name = separator < 0 ? group.Key : group.Key[(separator + 1)..] };
            foreach (var method in group) {
                service.Methods.Add(new() {
                    Name = method.Name, InputType = method.Request, OutputType = method.Response,
                    ServerStreaming = method.Kind is MethodType.ServerStreaming or MethodType.DuplexStreaming,
                    ClientStreaming = method.Kind is MethodType.ClientStreaming or MethodType.DuplexStreaming,
                });
            }
            options.Services.Add(service);
            var schema = model.GetSchema(options);
            var extra = new StringBuilder();
            var imports = new HashSet<string>(StringComparer.Ordinal);
            var reachable = new HashSet<Type>();
            foreach (var method in group) {
                Collect(method.Request);
                Collect(method.Response);
            }
            foreach (var meta in model.GetTypes().Cast<MetaType>().Where(meta => reachable.Contains(meta.Type) && meta.SerializerType is not null)) {
                try {
                    WriteSchema.Invoke(meta, [new HashSet<Type>(), extra, 0, imports, options.Syntax, options.Package, options.Flags]);
                } catch (TargetInvocationException exception) when (exception.InnerException is not null) {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }
            foreach (var import in imports.Where(import => !schema.Contains($"import \"{import}\";", StringComparison.Ordinal))) {
                extra.Insert(0, $"import \"{import}\";{Environment.NewLine}");
            }
            schema += extra;

            void Collect(Type type) {
                type = Nullable.GetUnderlyingType(type) ?? type;
                if (!reachable.Add(type)) return;
                if (type.IsArray) Collect(type.GetElementType()!);
                if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) Collect(argument);
                if (!model.IsDefined(type)) return;
                foreach (var field in model[type].GetFields()) Collect(field.MemberType);
            }
            var set = new ProtoReflection::Google.Protobuf.Reflection.FileDescriptorSet();
            set.Add($"{group.Key}.proto", true, new StringReader(schema));
            // The pinned emitter prefixes enum values but leaves proto2 defaults unprefixed.
            foreach (var file in set.Files) {
                var enums = new Dictionary<string, ProtoReflection::Google.Protobuf.Reflection.EnumDescriptorProto>(StringComparer.Ordinal);
                var messages = new List<(string Scope, ProtoReflection::Google.Protobuf.Reflection.DescriptorProto Message)>();
                Register(file.Package, file.MessageTypes, file.EnumTypes);
                foreach (var (scope, message) in messages) {
                    foreach (var field in message.Fields.Where(field => !string.IsNullOrEmpty(field.DefaultValue))) {
                        ProtoReflection::Google.Protobuf.Reflection.EnumDescriptorProto? enumeration = null;
                        if (field.TypeName.StartsWith(".", StringComparison.Ordinal)) {
                            enums.TryGetValue(field.TypeName[1..], out enumeration);
                        } else {
                            for (var current = scope; enumeration is null;) {
                                enums.TryGetValue(string.IsNullOrEmpty(current) ? field.TypeName : $"{current}.{field.TypeName}", out enumeration);
                                if (string.IsNullOrEmpty(current)) break;
                                var boundary = current.LastIndexOf('.');
                                current = boundary < 0 ? "" : current[..boundary];
                            }
                        }
                        if (enumeration is null || enumeration.Values.Any(value => value.Name == field.DefaultValue)) continue;
                        var enumDefault = enumeration.Values.Single(value => value.Name == $"{enumeration.Name}_{field.DefaultValue}");
                        field.DefaultValue = enumDefault.Name;
                    }
                }

                void Register(string scope,
                    IEnumerable<ProtoReflection::Google.Protobuf.Reflection.DescriptorProto> children,
                    IEnumerable<ProtoReflection::Google.Protobuf.Reflection.EnumDescriptorProto> definitions) {
                    foreach (var enumeration in definitions) enums.Add(string.IsNullOrEmpty(scope) ? enumeration.Name : $"{scope}.{enumeration.Name}", enumeration);
                    foreach (var message in children) {
                        var nested = string.IsNullOrEmpty(scope) ? message.Name : $"{scope}.{message.Name}";
                        messages.Add((nested, message));
                        Register(nested, message.NestedTypes, message.EnumTypes);
                    }
                }
            }
            set.Process();
            var errors = set.GetErrors().Where(error => !error.IsWarning).ToArray();
            if (errors.Length != 0) {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Select(error => error.Message)));
            }
            var files = new List<ByteString>();
            var definitions = set.Files.ToDictionary(file => file.Name, StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in set.Files) Emit(file.Name);

            void Emit(string name) {
                if (emitted.Contains(name)) return;
                if (!visiting.Add(name)) throw new InvalidOperationException($"Cyclic protobuf dependency '{name}'.");
                var file = definitions[name];
                foreach (var dependency in file.Dependencies) Emit(dependency);
                using var buffer = new MemoryStream();
                Serializer.Serialize(buffer, file);
                files.Add(ByteString.CopyFrom(buffer.GetBuffer(), 0, checked((int)buffer.Length)));
                visiting.Remove(name);
                emitted.Add(name);
            }
            results.AddRange(FileDescriptor.BuildFromByteStrings(files).SelectMany(file => file.Services));
        }
        return results;
    }
}
