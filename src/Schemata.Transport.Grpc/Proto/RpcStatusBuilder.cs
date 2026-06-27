using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Transport.Grpc.Proto;

/// <summary>Builds an AIP-193 <see cref="Google.Rpc.Status" /> from a <see cref="SchemataException" />.</summary>
internal static class RpcStatusBuilder
{
    /// <summary>
    ///     Builds a gRPC status message from a Schemata exception, request identifier, and
    ///     optional locale.
    /// </summary>
    /// <param name="ex">The Schemata exception.</param>
    /// <param name="requestId">The request identifier to include in error details.</param>
    /// <param name="locale">
    ///     Optional <seealso href="https://www.rfc-editor.org/rfc/bcp/bcp47.html">BCP-47</seealso>
    ///     language tag parsed from the gRPC <c>accept-language</c> metadata; flows through
    ///     <see cref="SchemataException.CreateErrorResponse" /> to attach a
    ///     <see cref="LocalizedMessageDetail" /> when resolvable.
    /// </param>
    /// <returns>The gRPC status message.</returns>
    public static Google.Rpc.Status Build(SchemataException ex, string? requestId, string? locale = null) {
        var response = ex.CreateErrorResponse(requestId, locale: locale);
        if (response is not ErrorResponse error) {
            return new() {
                Code = MapFromCanonical(ex.Status),
                Message = ex.Message,
            };
        }

        var status = new Google.Rpc.Status {
            Code    = MapFromCanonical(error.Error?.Status),
            Message = error.Error?.Message,
        };

        if (error.Error?.Details is null) {
            return status;
        }

        foreach (var detail in error.Error.Details) {
            var any = ToAny(detail);
            if (any is not null) {
                status.Details.Add(any);
            }
        }

        return status;
    }

    private static Any? ToAny(object detail) {
        return detail switch {
            BadRequestDetail d => Any.Pack(new BadRequest {
                FieldViolations = {
                    (d.FieldViolations ?? []).Select(fv => new BadRequest.Types.FieldViolation {
                        Field = fv.Field ?? "", Description = fv.Description ?? "",
                    }),
                },
            }),
            ErrorInfoDetail d => Any.Pack(new ErrorInfo {
                Reason = d.Reason ?? "", Domain = d.Domain ?? "", Metadata = { d.Metadata ?? [] },
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
                        Type = v.Type ?? "", Subject = v.Subject ?? "", Description = v.Description ?? "",
                    }),
                },
            }),
            QuotaFailureDetail d => Any.Pack(new QuotaFailure {
                Violations = {
                    (d.Violations ?? []).Select(v => new QuotaFailure.Types.Violation {
                        Subject = v.Subject ?? "", Description = v.Description ?? "",
                    }),
                },
            }),
            RequestInfoDetail d => Any.Pack(new RequestInfo {
                RequestId = d.RequestId ?? "", ServingData = d.ServingData ?? "",
            }),
            LocalizedMessageDetail d => Any.Pack(new LocalizedMessage {
                Locale = d.Locale ?? "", Message = d.Message ?? "",
            }),
            HelpDetail d => Any.Pack(new Help {
                Links = {
                    (d.Links ?? []).Select(l => new Help.Types.Link {
                        Description = l.Description ?? "", Url = l.Url ?? "",
                    }),
                },
            }),
            RetryInfoDetail d => Any.Pack(new RetryInfo {
                RetryDelay = d.RetryDelay is { } delay ? Duration.FromTimeSpan(delay) : null,
            }),
            DebugInfoDetail d => Any.Pack(new DebugInfo {
                StackEntries = { d.StackEntries ?? [] },
                Detail       = d.Detail ?? "",
            }),
            var _ => null,
        };
    }

    private static int MapFromCanonical(string? code) {
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
