using System;
using System.Collections.Generic;
using Schemata.Transport.Grpc.Proto;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Transport.Grpc;

namespace Schemata.Push.Grpc;

/// <summary>
///     The shared protobuf runtime model for the Push control-plane contract types. The model is
///     built once per host at <see cref="CompatibilityLevel.Level300" /> so the gRPC marshallers and
///     code-first clients serialize identical bytes.
/// </summary>
public sealed class PushGrpcModel
{
    /// <summary>The fully-qualified gRPC service name.</summary>
    public const string ServiceName = "schemata.push.v1.PushControl";

    private static readonly Type[] ContractTypes = [
        typeof(PushSubscriptionView),
        typeof(CreatePushSubscriptionCommand),
        typeof(ListPushSubscriptionsQuery),
        typeof(ListPushSubscriptionsResult),
        typeof(DeletePushSubscriptionCommand),
        typeof(DeletePushSubscriptionResult),
        typeof(SendPushOptions),
        typeof(SendPushCommand),
        typeof(TransportOutcome),
        typeof(SendPushResult),
    ];

    /// <summary>Initializes the model and registers every contract type.</summary>
    public PushGrpcModel() {
        Model = RuntimeTypeModel.Create();
        Model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        foreach (var type in ContractTypes) {
            Model.Add(type, true);
        }
        Create = Unary<CreatePushSubscriptionCommand, PushSubscriptionView>("Create");
        List = Unary<ListPushSubscriptionsQuery, ListPushSubscriptionsResult>("List");
        Delete = Unary<DeletePushSubscriptionCommand, DeletePushSubscriptionResult>("Delete");
        Send = Unary<SendPushCommand, SendPushResult>("Send");
        Methods = [GrpcMethodSchema.From(Create), GrpcMethodSchema.From(List), GrpcMethodSchema.From(Delete), GrpcMethodSchema.From(Send)];
    }

    public Method<CreatePushSubscriptionCommand, PushSubscriptionView> Create { get; }
    public Method<ListPushSubscriptionsQuery, ListPushSubscriptionsResult> List { get; }
    public Method<DeletePushSubscriptionCommand, DeletePushSubscriptionResult> Delete { get; }
    public Method<SendPushCommand, SendPushResult> Send { get; }

    public IReadOnlyList<GrpcMethodSchema> Methods { get; }

    private Method<TRequest, TResponse> Unary<TRequest, TResponse>(string name) =>
        new(MethodType.Unary, ServiceName, name, Marshaller<TRequest>(), Marshaller<TResponse>());

    /// <summary>The protobuf runtime model backing the control-plane marshallers.</summary>
    public RuntimeTypeModel Model { get; }

    /// <summary>Creates a marshaller for a contract type from the shared model.</summary>
    public Marshaller<T> Marshaller<T>() => GrpcMarshallers.Create<T>(Model);
}
