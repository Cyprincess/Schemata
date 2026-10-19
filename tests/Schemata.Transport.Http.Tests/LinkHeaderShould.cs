using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Transport.Http.Tests;

public class LinkHeaderShould
{
    private static readonly Uri Context = new("https://api.example.com/items?page=1");

    [Fact]
    public void Parse_A_Quoted_Link_Value() {
        var link = Assert.Single(LinkHeader.Parse(
            "<https://api.example.com/items?page=2>; rel=\"next\"; title=\"next page\"; type=\"text/html\"",
            Context));

        Assert.Equal("https://api.example.com/items?page=2", link.Target);
        Assert.Equal("next", link.Rel);
        Assert.Equal("next page", link.Title);
        Assert.Equal("text/html", link.Type);
        Assert.Null(link.Anchor);
        Assert.Null(link.Extensions);
    }

    [Fact]
    public void Parse_Multiple_Comma_Separated_Link_Values() {
        var links = LinkHeader.Parse(
            "<https://api.example.com/items?page=1>; rel=\"first\", <https://api.example.com/items?page=2>; rel=\"next\"",
            Context);

        Assert.Equal(2, links.Count);
        Assert.Equal("first", links[0].Rel);
        Assert.Equal("next", links[1].Rel);
        Assert.Equal("https://api.example.com/items?page=2", links[1].Target);
    }

    [Fact]
    public void Keep_Commas_And_Semicolons_Inside_Quoted_Parameters() {
        var link = Assert.Single(LinkHeader.Parse(
            "<https://api.example.com/items>; rel=\"next\"; title=\"a, b; c\"",
            Context));

        Assert.Equal("a, b; c", link.Title);
    }

    [Fact]
    public void Unescape_Quoted_String_Escapes() {
        var link = Assert.Single(LinkHeader.Parse(
            "</items>; rel=\"next\"; title=\"say \\\"hi\\\" \\\\ done\"",
            Context));

        Assert.Equal("say \"hi\" \\ done", link.Title);
    }

    [Fact]
    public void Accept_Token_Parameter_Values() {
        var link = Assert.Single(LinkHeader.Parse("</items>; rel=next; hreflang=en; type=\"text/html\"", Context));

        Assert.Equal("next", link.Rel);
        Assert.Equal("en", link.Hreflang);
        Assert.Equal("text/html", link.Type);
    }

    [Theory]
    [InlineData("items/2", "https://api.example.com/items/2")]
    [InlineData("../other", "https://api.example.com/other")]
    [InlineData("?page=2", "https://api.example.com/items?page=2")]
    [InlineData("/root", "https://api.example.com/root")]
    public void Resolve_Relative_Targets_Against_The_Context(string target, string expected) {
        var link = Assert.Single(LinkHeader.Parse($"<{target}>; rel=\"next\"", Context));

        Assert.Equal(expected, link.Target);
    }

    [Fact]
    public void Resolve_A_Relative_Anchor_Without_Changing_The_Target_Base() {
        var link = Assert.Single(LinkHeader.Parse("</terms>; rel=\"copyright\"; anchor=\"#foo\"", Context));

        Assert.Equal("https://api.example.com/terms", link.Target);
        Assert.Equal("https://api.example.com/items?page=1#foo", link.Anchor);
    }

    [Fact]
    public void Keep_An_Absolute_Anchor() {
        var link = Assert.Single(LinkHeader.Parse(
            "<https://example.com/terms>; rel=\"copyright\"; anchor=\"https://example.com/alternate\"",
            Context));

        Assert.Equal("https://example.com/alternate", link.Anchor);
    }

    [Fact]
    public void Expand_Multiple_Relation_Types_Into_One_Link_Per_Relation() {
        var links = LinkHeader.Parse("<https://api.example.com/items>; rel=\"Next LAST\"");

        Assert.Equal(new[] { "next", "last" }, links.Select(link => link.Rel));
        Assert.All(links, link => Assert.Equal("https://api.example.com/items", link.Target));
    }

    [Fact]
    public void Keep_The_First_Repeated_Serialization_Parameter() {
        var link = Assert.Single(LinkHeader.Parse(
            "<https://api.example.com/items>; rel=\"next\"; rel=\"prev\"; title=\"one\"; title=\"two\"",
            Context));

        Assert.Equal("next", link.Rel);
        Assert.Equal("one", link.Title);
    }

    [Fact]
    public void Keep_Extension_Parameters_With_Verbatim_Values() {
        var link = Assert.Single(LinkHeader.Parse(
            "<https://api.example.com/items>; rel=\"next\"; X-Custom=\"A Value\"; count=3",
            Context));

        Assert.NotNull(link.Extensions);
        Assert.Equal("A Value", link.Extensions["x-custom"]);
        Assert.Equal("3", link.Extensions["count"]);
    }

    [Fact]
    public void Serialize_Quoted_Parameters_And_Escape_Their_Content() {
        var header = LinkHeader.Serialize([
            new WebLink("https://api.example.com/items", "next", Title: "say \"hi\" \\ done"),
        ]);

        Assert.Equal("<https://api.example.com/items>; rel=\"next\"; title=\"say \\\"hi\\\" \\\\ done\"", header);
    }

    [Fact]
    public void Serialize_Each_Distinct_Link_Once() {
        var link = new WebLink("https://api.example.com/items", "self");
        var header = LinkHeader.Serialize([link, link]);

        Assert.DoesNotContain(",", header);
        Assert.Single(LinkHeader.Parse(header));
    }

    [Fact]
    public void Round_Trip_Serialized_Links() {
        var original = new[] {
            new WebLink("https://api.example.com/items?page=2", "next", Title: "next \"page\""),
            new WebLink("https://api.example.com/items?page=3", "last", Media: "screen", Type: "text/html",
                Extensions: new Dictionary<string, string> { ["count"] = "3" }),
        };

        var parsed = LinkHeader.Parse(LinkHeader.Serialize(original));

        Assert.Equal(2, parsed.Count);
        Assert.Equal(original[0], parsed[0]);
        Assert.Equal(original[1].Target, parsed[1].Target);
        Assert.Equal(original[1].Rel, parsed[1].Rel);
        Assert.Equal(original[1].Media, parsed[1].Media);
        Assert.Equal(original[1].Type, parsed[1].Type);
        Assert.Equal("3", parsed[1].Extensions!["count"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a link")]
    [InlineData("<unterminated")]
    [InlineData("<https://api.example.com/items>")]
    [InlineData("<http://[>; rel=\"next\"")]
    public void Round_Trip_Nothing_For_Malformed_Input(string? header) {
        Assert.Empty(LinkHeader.Parse(header, Context));
    }

    [Fact]
    public void Keep_The_Links_Parsed_Before_A_Syntax_Error() {
        var link = Assert.Single(LinkHeader.Parse("<https://api.example.com/items>; rel=\"next\", garbage", Context));

        Assert.Equal("next", link.Rel);
    }

    [Theory]
    [InlineData("https://api.example.com/a\r\nX-Injected: yes")]
    [InlineData("https://api.example.com/a b")]
    [InlineData("https://api.example.com/a{b}")]
    [InlineData("/a\\b")]
    [InlineData("/a{b}")]
    [InlineData("/a|b")]
    [InlineData("/a^b")]
    [InlineData("/a`b")]
    [InlineData("/a b")]
    [InlineData("/a%zz")]
    [InlineData("/日本語")]
    public void Reject_Targets_That_Are_Not_Uri_References(string target) {
        Assert.Throws<ArgumentException>(() => LinkHeader.Serialize([new WebLink(target, "self")]));
    }

    [Fact]
    public void Convert_An_Absolute_IRI_Target_To_A_Uri() {
        var header = LinkHeader.Serialize([new WebLink("https://example.com/日本語?q=ü", "self")]);

        Assert.Equal("<https://example.com/%E6%97%A5%E6%9C%AC%E8%AA%9E?q=%C3%BC>; rel=\"self\"", header);
    }

    [Fact]
    public void Normalize_An_Iri_Host_To_Punycode() {
        var header = LinkHeader.Serialize([new WebLink("https://例え.jp/x", "self")]);

        Assert.Equal("<https://xn--r8jz45g.jp/x>; rel=\"self\"", header);
    }

    [Fact]
    public void Serialize_Network_Path_And_Drive_Path_Targets_Without_A_File_Rewrite() {
        var header = LinkHeader.Serialize([
            new WebLink("//host/path", "related"),
            new WebLink("C:/a/b", "related"),
        ]);

        Assert.Equal("<//host/path>; rel=\"related\", <C:/a/b>; rel=\"related\"", header);
    }

    [Fact]
    public void Resolve_A_Network_Path_Target_Against_The_Context() {
        var link = Assert.Single(LinkHeader.Parse("<//cdn.example.com/items>; rel=\"related\"", Context));

        Assert.Equal("https://cdn.example.com/items", link.Target);
    }

    [Fact]
    public void Keep_A_Drive_Path_Target_Without_A_File_Rewrite() {
        var link = Assert.Single(LinkHeader.Parse("<C:/a/b>; rel=\"self\"", Context));

        Assert.Equal("C:/a/b", link.Target);
    }

    [Theory]
    [InlineData("https://\uFFFF.jp/x")]
    [InlineData("https://\u3000.jp/x")]
    [InlineData("https://a\u200Db.jp/x")]
    public void Keep_An_Unmapped_Idn_Host_When_Parsing(string target) {
        var link = Assert.Single(LinkHeader.Parse($"<{target}>; rel=\"self\"", Context));

        Assert.Equal(target, link.Target);
    }

    [Theory]
    [InlineData("https://\uFFFF.jp/x")]
    [InlineData("https://\u3000.jp/x")]
    [InlineData("https://a\u200Db.jp/x")]
    public void Reject_An_Unmapped_Idn_Host_When_Serializing(string target) {
        Assert.Throws<ArgumentException>(() => LinkHeader.Serialize([new WebLink(target, "self")]));
    }

    [Theory]
    [InlineData("C:\\a\\b")]
    [InlineData("C:/a b")]
    public void Reject_Invalid_Drive_Path_Targets_Consistently(string target) {
        Assert.Empty(LinkHeader.Parse($"<{target}>; rel=\"self\"", Context));
        Assert.Throws<ArgumentException>(() => LinkHeader.Serialize([new WebLink(target, "self")]));
    }

    [Fact]
    public void Reject_Parameter_Values_With_Control_Characters() {
        Assert.Throws<ArgumentException>(() =>
            LinkHeader.Serialize([new WebLink("https://api.example.com/a", "self", Title: "line\nbreak")]));
    }

    [Fact]
    public void Register_Contributors_As_An_Enumerable() {
        var services = new ServiceCollection();

        services.AddSchemataLinkContributor<FirstContributor>();
        services.AddSchemataLinkContributor<SecondContributor>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var contributors = scope.ServiceProvider.GetServices<ILinkContributor>()
                                .Select(contributor => contributor.GetType())
                                .ToArray();

        Assert.Equal(new[] { typeof(FirstContributor), typeof(SecondContributor) }, contributors);
    }

    private sealed class FirstContributor : ILinkContributor
    {
        public IEnumerable<WebLink> GetLinks(LinkContext _) => [];
    }

    private sealed class SecondContributor : ILinkContributor
    {
        public IEnumerable<WebLink> GetLinks(LinkContext _) => [];
    }
}
