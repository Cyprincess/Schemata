using System;
using System.Text.Json;
using Schemata.Core.Json;
using Xunit;

namespace Schemata.Core.Tests.Json;

public class JsonStringNumberConverterShould
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { JsonStringNumberConverter.Instance } };

    [Fact]
    public void Read_Accepts_Numbers_And_Strings() {
        Assert.Equal(42, JsonSerializer.Deserialize<long>("42", Options));
        Assert.Equal(42, JsonSerializer.Deserialize<long>("\"42\"", Options));
    }

    [Fact]
    public void Write_Serializes_As_A_String() {
        Assert.Equal("\"9007199254740993\"", JsonSerializer.Serialize(9007199254740993L, Options));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Read_Unsupported_Token_Kinds_Fail_With_The_Intended_Conversion_Error(string json) {
        // Before the fix, these token kinds reached reader.GetString() and failed with a
        // secondary InvalidOperationException instead of the intended JsonException.
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>(json, Options));
        Assert.DoesNotContain("GetString", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Unparseable_String_Fails_With_The_Intended_Conversion_Error() {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>("\"not-a-number\"", Options));
    }
}
