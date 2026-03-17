extern alias ProtoBufReflection;
using System;
using System.IO;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Reflection;
using ProtoBufReflection::Google.Protobuf.Reflection;

namespace Schemata.Resource.Grpc;

internal static class ReflectionServiceFactory
{
    private const string CodeProto = """
        syntax = "proto3";
        package google.rpc;
        enum Code {
          OK = 0;
          CANCELLED = 1;
          UNKNOWN = 2;
          INVALID_ARGUMENT = 3;
          DEADLINE_EXCEEDED = 4;
          NOT_FOUND = 5;
          ALREADY_EXISTS = 6;
          PERMISSION_DENIED = 7;
          RESOURCE_EXHAUSTED = 8;
          FAILED_PRECONDITION = 9;
          ABORTED = 10;
          OUT_OF_RANGE = 11;
          UNIMPLEMENTED = 12;
          INTERNAL = 13;
          UNAVAILABLE = 14;
          DATA_LOSS = 15;
          UNAUTHENTICATED = 16;
        }
        """;

    private const string StatusProto = """
        syntax = "proto3";
        package google.rpc;
        import "google/protobuf/any.proto";
        message Status {
          int32 code = 1;
          string message = 2;
          repeated google.protobuf.Any details = 3;
        }
        """;

    private const string ErrorDetailsProto = """
        syntax = "proto3";
        package google.rpc;
        import "google/protobuf/duration.proto";
        message ErrorInfo {
          string reason = 1;
          string domain = 2;
          map<string, string> metadata = 3;
        }
        message RetryInfo {
          google.protobuf.Duration retry_delay = 1;
        }
        message DebugInfo {
          repeated string stack_entries = 1;
          string detail = 2;
        }
        message QuotaFailure {
          message Violation {
            string subject = 1;
            string description = 2;
            string api_service = 3;
            string quota_metric = 4;
            string quota_id = 5;
            map<string, string> quota_dimensions = 6;
            int64 quota_value = 7;
            optional int64 future_quota_value = 8;
          }
          repeated Violation violations = 1;
        }
        message PreconditionFailure {
          message Violation {
            string type = 1;
            string subject = 2;
            string description = 3;
          }
          repeated Violation violations = 1;
        }
        message BadRequest {
          message FieldViolation {
            string field = 1;
            string description = 2;
            string reason = 3;
            LocalizedMessage localized_message = 4;
          }
          repeated FieldViolation field_violations = 1;
        }
        message RequestInfo {
          string request_id = 1;
          string serving_data = 2;
        }
        message ResourceInfo {
          string resource_type = 1;
          string resource_name = 2;
          string owner = 3;
          string description = 4;
        }
        message Help {
          message Link {
            string description = 1;
            string url = 2;
          }
          repeated Link links = 1;
        }
        message LocalizedMessage {
          string locale = 1;
          string message = 2;
        }
        """;

    public static ReflectionService Create(BinderConfiguration binder, Type[] serviceTypes) {
        var generator    = new SchemaGenerator { BinderConfiguration = binder };
        var serviceProto = generator.GetSchema(serviceTypes);

        var set = new FileDescriptorSet();

        // Add Google RPC protos
        AddProto(set, "google/rpc/code.proto", CodeProto);
        AddProto(set, "google/rpc/status.proto", StatusProto);
        AddProto(set, "google/rpc/error_details.proto", ErrorDetailsProto);

        // Add code-first service schema
        AddProto(set, "services.proto", serviceProto);

        set.Process();

        return new(set);
    }

    private static void AddProto(FileDescriptorSet set, string filename, string content) {
        using var reader = new StringReader(content);
        set.Add(filename, true, reader);
    }
}
