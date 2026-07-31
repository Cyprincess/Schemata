using System.Collections.Generic;
using System.ComponentModel;
using Schemata.Abstractions.Entities;
using Xunit;

namespace Schemata.Common.Tests;

public class DescriptiveExtensionsShould
{
    [Fact]
    public void Apply_Declared_Labels_To_An_Empty_Target() {
        var target = new Labelled();

        typeof(Declaration).ApplyLabels(target);

        Assert.Equal("Approval", target.DisplayName);
        Assert.Equal("Routes a request to an approver.", target.Description);
        Assert.Equal("审批", target.DisplayNames!["zh-Hans"]);
        Assert.Equal("把请求路由给审批人。", target.Descriptions!["zh-Hans"]);
    }

    [Fact]
    public void Keep_An_Unlocalized_Label_The_Target_Already_Carries() {
        var target = new Labelled { DisplayName = "Assigned in code", Description = "Also in code" };

        typeof(Declaration).ApplyLabels(target);

        Assert.Equal("Assigned in code", target.DisplayName);
        Assert.Equal("Also in code", target.Description);
    }

    [Fact]
    public void Keep_A_Locale_The_Target_Already_Carries() {
        var target = new Labelled {
            DisplayNames = new() { ["zh-Hans"] = "代码里的名称" },
            Descriptions = new() { ["zh-Hans"] = "代码里的描述" },
        };

        typeof(Declaration).ApplyLabels(target);

        Assert.Equal("代码里的名称", target.DisplayNames!["zh-Hans"]);
        Assert.Equal("代码里的描述", target.Descriptions!["zh-Hans"]);
    }

    [Fact]
    public void Match_A_Declared_Locale_Whatever_Its_Casing() {
        var target = new Labelled();

        typeof(Declaration).ApplyLabels(target);

        Assert.Equal("审批", target.DisplayNames!["ZH-HANS"]);
    }

    [DisplayName("Approval")]
    [Description("Routes a request to an approver.")]
    [Localized("zh-Hans", "审批", "把请求路由给审批人。")]
    private sealed class Declaration;

    private sealed class Labelled : IDescriptive
    {
        #region IDescriptive Members

        public string?                     DisplayName  { get; set; }
        public Dictionary<string, string?>? DisplayNames { get; set; }
        public string?                     Description  { get; set; }
        public Dictionary<string, string?>? Descriptions { get; set; }

        #endregion
    }
}
