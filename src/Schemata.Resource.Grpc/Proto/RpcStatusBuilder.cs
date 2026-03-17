using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Grpc.Proto;

internal static class RpcStatusBuilder
{
    public static Google.Rpc.Status Build(SchemataException ex, string? requestId) {
        var status = new Google.Rpc.Status {
            Code    = MapFromCode(ex.Code),
            Message = ex.Message,
        };

        if (ex.Details is { Count: > 0 }) {
            foreach (var detail in ex.Details) {
                var any = ToAny(detail);
                if (any is not null) {
                    status.Details.Add(any);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(requestId)) {
            status.Details.Add(ToAny(new RequestInfoDetail { RequestId = requestId })!);
        }

        return status;
    }

    private static Any? ToAny(object detail) {
        return detail switch {
            BadRequestDetail d => Any.Pack(new BadRequest {
                FieldViolations = {
                    (d.FieldViolations ?? []).Select(fv => new BadRequest.Types.FieldViolation {
                        Field       = fv.Field ?? "",
                        Description = fv.Description ?? "",
                    }),
                },
            }),
            ErrorInfoDetail d => Any.Pack(new ErrorInfo {
                Reason   = d.Reason ?? "",
                Domain   = d.Domain ?? "",
                Metadata = { d.Metadata ?? [] },
            }),
            ResourceInfoDetail d => Any.Pack(new ResourceInfo {
                ResourceType = d.ResourceType ?? "",
                ResourceName = d.ResourceName ?? "",
                Owner        = d.Owner ?? "",
                Description  = d.Description ?? "",
            }),
            PreconditionFailureDetail d => Any.Pack(new PreconditionFailure {
                Violations = {
                    (d.Violations ?? []).Select(v => new PreconditionFailure.Types.Violation {
                        Type        = v.Type ?? "",
                        Subject     = v.Subject ?? "",
                        Description = v.Description ?? "",
                    }),
                },
            }),
            QuotaFailureDetail d => Any.Pack(new QuotaFailure {
                Violations = {
                    (d.Violations ?? []).Select(v => new QuotaFailure.Types.Violation {
                        Subject     = v.Subject ?? "",
                        Description = v.Description ?? "",
                    }),
                },
            }),
            RequestInfoDetail d => Any.Pack(new RequestInfo {
                RequestId   = d.RequestId ?? "",
                ServingData = d.ServingData ?? "",
            }),
            var _ => null,
        };
    }

    private static int MapFromCode(string? code) {
        return (int)(code switch {
            ErrorCodes.Ok                 => StatusCode.OK,
            ErrorCodes.InvalidArgument    => StatusCode.InvalidArgument,
            ErrorCodes.NotFound           => StatusCode.NotFound,
            ErrorCodes.PermissionDenied   => StatusCode.PermissionDenied,
            ErrorCodes.Aborted            => StatusCode.Aborted,
            ErrorCodes.AlreadyExists      => StatusCode.AlreadyExists,
            ErrorCodes.FailedPrecondition => StatusCode.FailedPrecondition,
            ErrorCodes.Unauthenticated    => StatusCode.Unauthenticated,
            ErrorCodes.ResourceExhausted  => StatusCode.ResourceExhausted,
            var _                         => StatusCode.Internal,
        });
    }
}
