using System;
using Schemata.Identity.Foundation.Services;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public sealed class PhoneNumberNormalizerShould
{
    [Theory]
    [InlineData("+14155550123", "14155550123")]
    [InlineData("+441234567890123", "441234567890123")]
    [InlineData("+1", "1")]
    public void Full_E164_Returns_Canonical_Digits(string input, string expected) {
        var normalizer = new E164PhoneNumberNormalizer();

        Assert.Equal(expected, normalizer.NormalizePhoneNumber(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("14155550123")]
    [InlineData("++14155550123")]
    [InlineData("+04155550123")]
    [InlineData("+1 4155550123")]
    [InlineData("+1-415-555-0123")]
    [InlineData("+14155550123x")]
    [InlineData("+1234567890123456")]
    public void Invalid_E164_Is_Rejected(string? input) {
        var normalizer = new E164PhoneNumberNormalizer();

        Assert.Throws<FormatException>(() => normalizer.NormalizePhoneNumber(input!));
    }
}
