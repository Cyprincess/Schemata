using System.Globalization;
using Schemata.Identity.Foundation.Services;
using Xunit;

namespace Schemata.Identity.Tests;

public class SchemataErrorDescriberShould
{
    private readonly SchemataErrorDescriber _describer = new();

    [Fact]
    public void PasswordTooShort_ForInvariantCulture_RendersEnglishTemplate() {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try {
            var error = _describer.PasswordTooShort(6);
            Assert.Equal("PasswordTooShort", error.Code);
            Assert.Contains("6", error.Description);
        } finally {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void PasswordTooShort_ForLocalizedCulture_RendersTranslatedTemplate() {
        var previous = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var baseline = _describer.PasswordTooShort(6).Description;

        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
        try {
            var error = _describer.PasswordTooShort(6);
            Assert.NotEqual(baseline, error.Description);
        } finally {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Describer_Overrides_PreserveDefaultCodes() {
        Assert.Equal("DefaultError", _describer.DefaultError().Code);
        Assert.Equal("DuplicateEmail", _describer.DuplicateEmail("a@b.c").Code);
        Assert.Equal("PasswordMismatch", _describer.PasswordMismatch().Code);
    }
}
