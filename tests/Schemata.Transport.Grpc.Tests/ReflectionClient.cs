using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Reflection.V1Alpha;

namespace Schemata.Transport.Grpc.Tests;

public static class ReflectionClient
{
    public static async Task<ServiceDescriptor> Discover(CallInvoker invoker, string service) {
        var client = new ServerReflection.ServerReflectionClient(invoker);
        using var call = client.ServerReflectionInfo();
        await call.RequestStream.WriteAsync(new() { FileContainingSymbol = service });
        await call.RequestStream.CompleteAsync();
        if (!await call.ResponseStream.MoveNext(CancellationToken.None)) throw new InvalidOperationException("Reflection returned no response.");
        var response = call.ResponseStream.Current;
        if (response.ErrorResponse is { } error) throw new InvalidOperationException(error.ErrorMessage);
        var definitions = response.FileDescriptorResponse.FileDescriptorProto.Select(bytes => (Bytes: bytes, Proto: FileDescriptorProto.Parser.ParseFrom(bytes)))
            .ToDictionary(file => file.Proto.Name, StringComparer.Ordinal);
        var ordered = new List<ByteString>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in definitions.Keys) Emit(name);
        return FileDescriptor.BuildFromByteStrings(ordered).SelectMany(file => file.Services)
            .Single(descriptor => descriptor.FullName == service);

        void Emit(string name) {
            if (emitted.Contains(name)) return;
            if (!visiting.Add(name)) throw new InvalidOperationException($"Cyclic reflected dependency '{name}'.");
            var file = definitions[name];
            foreach (var dependency in file.Proto.Dependency) Emit(dependency);
            ordered.Add(file.Bytes);
            visiting.Remove(name);
            emitted.Add(name);
        }
    }

    public static Method<byte[], byte[]> Method(MethodDescriptor method) => new(
        method.IsClientStreaming ? method.IsServerStreaming ? MethodType.DuplexStreaming : MethodType.ClientStreaming
            : method.IsServerStreaming ? MethodType.ServerStreaming : MethodType.Unary,
        method.Service.FullName, method.Name, new(bytes => bytes, bytes => bytes), new(bytes => bytes, bytes => bytes));

    public static byte[] Encode(MessageDescriptor descriptor, IReadOnlyDictionary<string, object?> values) {
        using var buffer = new MemoryStream();
        using var writer = new CodedOutputStream(buffer, true);
        foreach (var (name, value) in values) {
            var field = descriptor.FindFieldByName(name) ?? throw new InvalidOperationException($"Missing field {descriptor.FullName}.{name}.");
            Write(writer, field, value);
        }
        writer.Flush();
        return buffer.ToArray();
    }


    public static Dictionary<string, List<object>> Decode(MessageDescriptor descriptor, byte[] bytes) {
        var result = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var reader = new CodedInputStream(bytes);
        uint tag;
        while ((tag = reader.ReadTag()) != 0) {
            var field = descriptor.FindFieldByNumber(WireFormat.GetTagFieldNumber(tag));
            if (field is null) { reader.SkipLastField(); continue; }
            object value = field.FieldType switch {
                FieldType.Message => Decode(field.MessageType, reader.ReadBytes().ToByteArray()),
                FieldType.String => reader.ReadString(),
                FieldType.Bytes => reader.ReadBytes().ToByteArray(),
                FieldType.Bool => reader.ReadBool(),
                FieldType.Int32 or FieldType.Enum => reader.ReadInt32(),
                FieldType.Int64 => reader.ReadInt64(),
                FieldType.UInt64 => reader.ReadUInt64(),
                FieldType.SInt64 => reader.ReadSInt64(),
                FieldType.Double => reader.ReadDouble(),
                _ => throw new NotSupportedException(field.FullName),
            };
            if (!result.TryGetValue(field.Name, out var values)) result[field.Name] = values = [];
            values.Add(value);
        }
        return result;
    }
    private static void Write(CodedOutputStream writer, FieldDescriptor field, object? value) {
        if (field.IsRepeated && value is IEnumerable<object?> items) {
            foreach (var item in items) WriteOne(writer, field, item);
        } else WriteOne(writer, field, value);
    }

    private static void WriteOne(CodedOutputStream writer, FieldDescriptor field, object? value) {
        if (!field.IsRepeated && !field.HasPresence && value is not null) {
            if (field.FieldType == FieldType.Bool && !(bool)value) return;
            if (field.FieldType is FieldType.Enum or FieldType.Int32 or FieldType.Int64 or FieldType.SInt64 or FieldType.UInt64
                && Convert.ToDecimal(value) == 0) return;
        }
        switch (field.FieldType) {
            case FieldType.Message:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                writer.WriteBytes(ByteString.CopyFrom(Encode(field.MessageType, (IReadOnlyDictionary<string, object?>)value!)));
                break;
            case FieldType.String:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited); writer.WriteString((string)value!); break;
            case FieldType.Bool:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.Varint); writer.WriteBool((bool)value!); break;
            case FieldType.Int32:
            case FieldType.Enum:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.Varint); writer.WriteInt32(Convert.ToInt32(value)); break;
            case FieldType.Int64:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.Varint); writer.WriteInt64(Convert.ToInt64(value)); break;
            case FieldType.UInt64:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.Varint); writer.WriteUInt64(Convert.ToUInt64(value)); break;
            case FieldType.SInt64:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.Varint); writer.WriteSInt64(Convert.ToInt64(value)); break;
            case FieldType.Double:
                writer.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64); writer.WriteDouble(Convert.ToDouble(value)); break;
            default: throw new NotSupportedException($"Smoke input field {field.FullName}: {field.FieldType}.");
        }
    }
}
