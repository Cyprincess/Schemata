using System.ComponentModel;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessDefinitionLabelIdentityShould
{
    [Fact]
    public void KeepThePropertyNameAsIdentity_WhenALabelIsDeclared() {
        var definition = new LabelledProcess();

        var approval = Assert.Single(definition.Elements);

        Assert.Equal("Approval", approval.Name);
        Assert.Equal("Approval task", approval.DisplayName);
        Assert.Equal("Routes the request to an approver.", approval.Description);
        Assert.Equal("审批", approval.DisplayNames!["zh-Hans"]);
        Assert.Equal("把请求路由给审批人。", approval.Descriptions!["zh-Hans"]);
    }

    [Fact]
    public void LabelEventDefinitionsDeclaredAsMagicProperties() {
        var definition = new LabelledProcess();

        var message = Assert.Single(definition.Messages);

        Assert.Equal("Nudge", message.Name);
        Assert.Equal("催办", message.DisplayNames!["zh-Hans"]);
    }

    [Fact]
    public void LabelTheDefinitionItself_WhenDeclaredOnTheDefinitionClass() {
        var definition = new LabelledDefinition();

        Assert.Equal("Expense approval", definition.DisplayName);
        Assert.Equal("Routes an expense claim to an approver.", definition.Description);
        Assert.Equal("费用审批", definition.DisplayNames!["zh-Hans"]);
        Assert.Equal("把费用申请路由给审批人。", definition.Descriptions!["zh-Hans"]);
    }

    [DisplayName("Expense approval")]
    [Description("Routes an expense claim to an approver.")]
    [Localized("zh-Hans", "费用审批", "把费用申请路由给审批人。")]
    private sealed class LabelledDefinition : ProcessDefinition
    {
        public UserTask Approval { get; private set; } = null!;
    }

    private sealed class LabelledProcess : ProcessDefinition
    {
        [DisplayName("Approval task")]
        [Description("Routes the request to an approver.")]
        [Localized("zh-Hans", "审批", "把请求路由给审批人。")]
        public UserTask Approval { get; private set; } = null!;

        [Localized("zh-Hans", "催办", "催办消息")]
        public Message Nudge { get; private set; } = null!;
    }
}