using System.Text.Json;
using Schemata.Abstractions.Resource;
using Xunit;

namespace Schemata.Abstractions.Tests;

public class RawJsonConverterShould
{
    [Theory]
    [InlineData("\"123\"")]
    [InlineData("\"null\"")]
    [InlineData("\"{}\"")]
    [InlineData("\"true\"")]
    public void StringTokens_WithJsonLookingContent_StayStrings(string token) {
        var response = JsonSerializer.Deserialize<OperationResponse>($"{{\"Output\":{token}}}");
        Assert.NotNull(response);
        Assert.Equal(token, response.Output);

        var round = OutputOf($"{{\"Output\":{token}}}");
        Assert.Equal(JsonValueKind.String, round.ValueKind);
    }

    [Theory]
    [InlineData("123", JsonValueKind.Number)]
    [InlineData("1.5", JsonValueKind.Number)]
    [InlineData("true", JsonValueKind.True)]
    [InlineData("false", JsonValueKind.False)]
    [InlineData("{\"a\":1}", JsonValueKind.Object)]
    [InlineData("[1,2]", JsonValueKind.Array)]
    public void NonStringTokens_RetainTheirType(string token, JsonValueKind kind) {
        Assert.Equal(kind, OutputOf($"{{\"Output\":{token}}}").ValueKind);
    }

    [Fact]
    public void NullToken_RoundTripsAsNull() {
        var response = JsonSerializer.Deserialize<OperationResponse>("{\"Output\":null}");
        Assert.Null(response!.Output);

        var json   = JsonSerializer.Serialize(new OperationResponse());
        var output = JsonDocument.Parse(json).RootElement.GetProperty("Output");
        Assert.Equal(JsonValueKind.Null, output.ValueKind);
    }

    [Fact]
    public void InvalidJsonValue_IsEmittedAsPlainString() {
        var json   = JsonSerializer.Serialize(new OperationResponse { Output = "not json" });
        var output = JsonDocument.Parse(json).RootElement.GetProperty("Output");

        Assert.Equal(JsonValueKind.String, output.ValueKind);
        Assert.Equal("not json", output.GetString());
    }

    private static JsonElement OutputOf(string wire) {
        var round = JsonSerializer.Serialize(JsonSerializer.Deserialize<OperationResponse>(wire));
        return JsonDocument.Parse(round).RootElement.GetProperty("Output");
    }
}
