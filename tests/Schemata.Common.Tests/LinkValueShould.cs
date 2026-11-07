using System;
using System.Collections.Generic;
using Schemata.Abstractions.Linking;
using Schemata.Common.Linking;
using Xunit;

namespace Schemata.Common.Tests;

[Trait("Layer", "Unit")]
public class LinkValueShould
{
    [Theory]
    [InlineData("UTF-8'en'%C2%A3%20rates")]
    [InlineData("utf-8''%c2%a3%20rates")]
    [InlineData("UTF-8'en-US'rates")]
    public void Serialize_TitleStar_EmitsUnquotedExtValue(string value) =>
        Assert.Equal("</jobs/1>; rel=\"related\"; title*=" + value,
            LinkValue.Serialize(new WebLink("/jobs/1", "related", TitleStar: value)));

    [Theory]
    [InlineData("ISO-8859-1'en'%A3")]
    [InlineData("UTF-8'en..US'rates")]
    [InlineData("UTF-8'en'%")]
    [InlineData("UTF-8'en'%a")]
    [InlineData("UTF-8'en'%xx")]
    [InlineData("UTF-8'en'£")]
    [InlineData("UTF-8'en'%ff")]
    [InlineData("UTF-8'en'%0A")]
    [InlineData("UTF-8'en'rates\n")]
    public void Serialize_Invalid_ExtValue_RejectsInput(string value) =>
        Assert.Throws<ArgumentException>(() => LinkValue.Serialize(new WebLink("/jobs/1", "related", TitleStar: value)));

    [Theory]
    [InlineData("bad\nvalue")]
    [InlineData("bad\0value")]
    [InlineData("bad\u007fvalue")]
    public void Serialize_Invalid_Title_And_Input_Remain_Strict(string value) {
        var title = new WebLink("/items/one", "related", Title: value);
        var input = new WebLink("/service/Read", "related", Extensions: new Dictionary<string, string> { ["filter"] = value });
        Assert.False(LinkValue.IsRepresentable(title));
        Assert.False(LinkValue.IsRepresentable(input));
        Assert.Throws<ArgumentException>(() => LinkValue.Serialize(title));
        Assert.Throws<ArgumentException>(() => LinkValue.Serialize(input));
    }

    [Theory]
    [InlineData("rev")]
    [InlineData("REV")]
    [InlineData("title*")]
    [InlineData("rel")]
    public void Serialize_ReservedExtension_RejectsCollision(string name) =>
        Assert.Throws<ArgumentException>(() => LinkValue.Serialize(new WebLink("/jobs/1", "related",
            Extensions: new Dictionary<string, string> { [name] = "up" })));
}
