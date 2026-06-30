using System.Collections.Generic;
using System.Globalization;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Xunit;

namespace Schemata.Common.Tests.Errors;

public class LocalizedMessageFormatterShould
{
    [Fact]
    public void Format_NamedTemplate_SubstitutesByKey() {
        var rendered = LocalizedMessageFormatter.Format(
            "Resource '{name}' rejected by '{owner}'.",
            new Dictionary<string, string> { ["name"] = "Book", ["owner"] = "Hugo" });

        Assert.Equal("Resource 'Book' rejected by 'Hugo'.", rendered);
    }

    [Fact]
    public void Format_NamedTemplate_MissingKey_LeavesPlaceholderLiteral() {
        var rendered = LocalizedMessageFormatter.Format(
            "Resource '{name}' rejected by '{owner}'.",
            new Dictionary<string, string> { ["name"] = "Book" });

        Assert.Equal("Resource 'Book' rejected by '{owner}'.", rendered);
    }

    [Fact]
    public void Format_PositionalTemplate_HonorsInsertionOrder() {
        var rendered = LocalizedMessageFormatter.Format(
            "{0} requires {1}.",
            new Dictionary<string, string> { ["a"] = "Schemata", ["b"] = "AIP-122" },
            CultureInfo.InvariantCulture);

        Assert.Equal("Schemata requires AIP-122.", rendered);
    }

    [Fact]
    public void Format_PositionalTemplate_NullArgs_ReturnsTemplate() {
        var rendered = LocalizedMessageFormatter.Format("static message", null);

        Assert.Equal("static message", rendered);
    }

    [Fact]
    public void Format_PositionalTemplate_FormatException_FallsBackToTemplate() {
        // Positional template with unmatched placeholders against a sparse arg list
        // should not throw; the helper returns the template verbatim instead.
        var rendered = LocalizedMessageFormatter.Format(
            "{0} {1} {2}",
            new Dictionary<string, string> { ["a"] = "only-one" },
            CultureInfo.InvariantCulture);

        Assert.Equal("{0} {1} {2}", rendered);
    }

    [Fact]
    public void Format_NullOrEmptyTemplate_ReturnsAsIs() {
        Assert.Null(LocalizedMessageFormatter.Format(null, null));
        Assert.Equal(string.Empty, LocalizedMessageFormatter.Format(string.Empty, null));
    }

    [Fact]
    public void FormatInvariant_ResolvesResourceKey_AndSubstitutesArgs() {
        var rendered = LocalizedMessageFormatter.FormatInvariant(
            SchemataResources.INVALID_PARENT,
            new Dictionary<string, string?> { ["parent"] = "tenants/acme" });

        Assert.Equal("The parent 'tenants/acme' is invalid.", rendered);
    }

    [Fact]
    public void FormatInvariant_UnknownResourceKey_ReturnsNull() {
        var rendered = LocalizedMessageFormatter.FormatInvariant("NO_SUCH_RESX_KEY");

        Assert.Null(rendered);
    }
}
