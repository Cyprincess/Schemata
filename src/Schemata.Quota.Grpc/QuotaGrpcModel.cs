using System;
using System.Collections.Generic;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Quota.Grpc;

public sealed class QuotaGrpcModel
{
    public const string ServiceName = "schemata.quota.v1.QuotaControl";

    private static readonly Type[] ContractTypes = [
        typeof(ReserveQuotaCommand),
        typeof(SettleQuotaCommand),
        typeof(ListQuotaLedgerCommand),
        typeof(QuotaChargeWire),
        typeof(QuotaPolicyWire),
        typeof(QuotaReservationWire),
        typeof(QuotaAccountWire),
        typeof(QuotaLedgerWire),
        typeof(QuotaLedgerEntryWire),
    ];

    public QuotaGrpcModel() {
        Model = RuntimeTypeModel.Create();
        Model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        foreach (var type in ContractTypes) Model.Add(type, true);
        Reserve = Unary<ReserveQuotaCommand, QuotaReservationWire>("Reserve");
        Commit = Unary<SettleQuotaCommand, QuotaReservationWire>("Commit");
        Release = Unary<SettleQuotaCommand, QuotaReservationWire>("Release");
        ListLedger = Unary<ListQuotaLedgerCommand, QuotaLedgerWire>("ListLedger");
        Methods = [GrpcMethodSchema.From(Reserve), GrpcMethodSchema.From(Commit), GrpcMethodSchema.From(Release),
            GrpcMethodSchema.From(ListLedger)];
    }

    public RuntimeTypeModel Model { get; }
    public Method<ReserveQuotaCommand, QuotaReservationWire> Reserve { get; }
    public Method<SettleQuotaCommand, QuotaReservationWire> Commit { get; }
    public Method<SettleQuotaCommand, QuotaReservationWire> Release { get; }
    public Method<ListQuotaLedgerCommand, QuotaLedgerWire> ListLedger { get; }
    public IReadOnlyList<GrpcMethodSchema> Methods { get; }

    public Marshaller<T> Marshaller<T>() => GrpcMarshallers.Create<T>(Model);

    private Method<TRequest, TResponse> Unary<TRequest, TResponse>(string name) =>
        new(MethodType.Unary, ServiceName, name, Marshaller<TRequest>(), Marshaller<TResponse>());
}
