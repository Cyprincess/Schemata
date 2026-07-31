using System.Linq;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public class DslLocalizedLabelShould
{
    [Fact]
    public void LocalizeASynthesizedEndEvent() {
        var definition = new LocalizedProcess();

        var end = definition.AllElements.Single(e => e.Name == "End_Review");

        Assert.Equal("Finished", end.DisplayName);
        Assert.Equal("完成", end.DisplayNames!["zh-Hans"]);
        Assert.Equal("流程结束。", end.Descriptions!["zh-Hans"]);
    }

    [Fact]
    public void LocalizeASynthesizedBoundaryEvent() {
        var definition = new LocalizedProcess();

        var boundary = definition.AllElements.Single(e => e.Name.StartsWith("Catch_Review_"));

        Assert.Equal("Timed out", boundary.DisplayName);
        Assert.Equal("超时", boundary.DisplayNames!["zh-Hans"]);
        Assert.Equal("已超过处理时限。", boundary.Descriptions!["zh-Hans"]);
    }

    [Fact]
    public void LocalizeAGatewayEdge() {
        var definition = new LocalizedProcess();

        var edge = definition.AllFlows.Single(f => f.Source.Name == "Decision_Triage" && f.Target.Name == "Escalate");

        Assert.Equal("Over limit", edge.DisplayName);
        Assert.Equal("金额超限", edge.DisplayNames!["zh-Hans"]);
        Assert.Equal("超过审批额度。", edge.Descriptions!["zh-Hans"]);
    }

    [Fact]
    public void LeaveTheMapsAloneForTheUnlocalizedOverload() {
        var definition = new LocalizedProcess();

        var gateway = definition.AllElements.Single(e => e.Name == "Decision_Triage");

        Assert.Equal("Triage decision", gateway.DisplayName);
        Assert.Null(gateway.DisplayNames);
        Assert.Null(gateway.Descriptions);
    }

    private sealed class LocalizedProcess : ProcessDefinition
    {
        public LocalizedProcess() {
            this.Start().Go(Triage);

            this.During(Triage)
                .Decide(new Branch(Escalate).Labelled("Over limit").Localized("zh-Hans", "金额超限", "超过审批额度。"))
                .Labelled("Triage decision");

            this.During(Escalate).Go(Review);

            this.During(Review)
                .OnTimer(System.TimeSpan.FromHours(1))
                .Labelled("Timed out")
                .Localized("zh-Hans", "超时", "已超过处理时限。")
                .Go(Escalate);

            this.During(Review)
                .End()
                .Labelled("Finished")
                .Localized("zh-Hans", "完成", "流程结束。");
        }

        public NoneTask Triage   { get; private set; } = null!;
        public UserTask Escalate { get; private set; } = null!;
        public UserTask Review   { get; private set; } = null!;
    }
}
