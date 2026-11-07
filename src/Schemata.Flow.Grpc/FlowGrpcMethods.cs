using System.Collections.Generic;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Grpc.Wire;
using Schemata.Flow.Skeleton.Models;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Flow.Grpc;

public static class FlowGrpcMethods
{
    public const string ServiceName = "schemata.flow.v1.ProcessDefinitionService";
    public static RuntimeTypeModel Model { get; } = CreateModel();
    public static readonly Method<ListProcessDefinitionsRequest, ListResultBase<ProcessDefinitionInfo, ProcessDefinitionInfo>> Definitions = new(
        MethodType.Unary, ServiceName, "ListProcessDefinitions",
        GrpcMarshallers.Create<ListProcessDefinitionsRequest>(Model),
        GrpcMarshallers.Create<ListResultBase<ProcessDefinitionInfo, ProcessDefinitionInfo>>(Model));
    public static IReadOnlyList<GrpcMethodSchema> Methods { get; } = [GrpcMethodSchema.From(Definitions)];

    private static RuntimeTypeModel CreateModel() {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ListProcessDefinitionsRequest));
        SchemataProtoModelConfigurator.ConfigureListTypes(model, [(typeof(ProcessDefinitionInfo), typeof(ProcessDefinitionInfo))]);
        return model;
    }
}
