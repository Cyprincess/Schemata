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
    [InlineData(typeof(ProcessDefinitionFlowInfo))]
    [InlineData(typeof(ProcessDefinitionMessageInfo))]
    public void CarryLocalizedLabelsOverGrpcAsProto3Maps(System.Type type) {
        var model = RuntimeTypeModel.Create();
        SchemataProtoModelConfigurator.ConfigureType(model, type);

        var fields = model[type].GetFields().ToDictionary(f => f.Member.Name, f => f);

        Assert.True(fields["DisplayNames"].IsMap);
        Assert.True(fields["Descriptions"].IsMap);
        Assert.Equal("display_names", fields["DisplayNames"].Name);
        Assert.Equal("descriptions", fields["Descriptions"].Name);
    }

    private static ProcessDefinitionInfo Sample() {
        return new() {
            CanonicalName = "definitions/orders",
            DisplayName   = "Approval",
            DisplayNames  = new() { ["zh-Hans"] = "审批" },
            Descriptions  = new() { ["zh-Hans"] = "把请求路由给审批人。" },
            Elements = [
                new() {
                    Name         = "Approval",
                    Kind         = nameof(UserTask),
                    DisplayName  = "Approval task",
                    DisplayNames = new() { ["zh-Hans"] = "审批任务" },
                },
            ],
            Flows = [
                new() {
                    Source       = "Approval",
                    Target       = "Done",
                    DisplayName  = "Over limit",
                    DisplayNames = new() { ["zh-Hans"] = "金额超限" },
                },
            ],
            Messages = [
                new() {
                    Name         = "nudge",
                    DisplayName  = "Nudge",
                    DisplayNames = new() { ["zh-Hans"] = "催办消息" },
                },
            ],
        };
    }
}
