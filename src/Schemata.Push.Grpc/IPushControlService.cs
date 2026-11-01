using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace Schemata.Push.Grpc;

/// <summary>
///     Code-first gRPC contract for the Push control plane. The subscription owner is derived
///     from the authenticated caller and never accepted from request input; provider
///     credentials are input-only.
/// </summary>
[Service(PushGrpcModel.ServiceName)]
public interface IPushControlService
{
    /// <summary>Creates a subscription for the calling owner; an identical address returns the existing row.</summary>
    [Operation]
    ValueTask<PushSubscriptionView> CreateAsync(CreatePushSubscriptionCommand request, CallContext context = default);

    /// <summary>Lists the calling owner's subscriptions, optionally narrowed to one transport.</summary>
    [Operation]
    ValueTask<ListPushSubscriptionsResult> ListAsync(ListPushSubscriptionsQuery request, CallContext context = default);

    /// <summary>Deletes the calling owner's subscription identified by transport and endpoint.</summary>
    [Operation]
    ValueTask<DeletePushSubscriptionResult> DeleteAsync(DeletePushSubscriptionCommand request, CallContext context = default);

    /// <summary>
    ///     Fans a dispatch out through every registered transport and returns each outcome.
    /// </summary>
    [Operation]
    ValueTask<SendPushResult> SendAsync(SendPushCommand request, CallContext context = default);
}
