using System.Linq;
using ProtoBuf.Meta;
using Schemata.Flow.Skeleton.Models;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessDefinitionLabelWireShould
{
    [Theory]
    [InlineData(typeof(ProcessDefinitionInfo))]
    [InlineData(typeof(ProcessDefinitionElementInfo))]
    [InlineData(typeof(ProcessDefinitionMessageInfo))]
    public void Carry_Localized_Labels_Over_Grpc_As_Proto3_Maps(System.Type type) {
        var model = RuntimeTypeModel.Create();
        SchemataProtoModelConfigurator.ConfigureType(model, type);

        var fields = model[type].GetFields().ToDictionary(f => f.Member.Name, f => f);

        Assert.True(fields["DisplayNames"].IsMap);
        Assert.True(fields["Descriptions"].IsMap);
        Assert.Equal("display_names", fields["DisplayNames"].Name);
        Assert.Equal("descriptions", fields["Descriptions"].Name);
    }
}
