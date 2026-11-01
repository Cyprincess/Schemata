using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Schemata.Common;
using Xunit;

namespace Schemata.Common.Tests;

[Trait("Layer", "Integration")]
public sealed class ProtectedContinuationShould
{
    [Fact]
    public void Preserve_Typed_Payload_With_Internal_JSON_Options() {
        var protector = Protector();
        var input = new Payload("reports/A/snapshots/daily", 3, new[] { "value", "nested" });
        var decoded = ProtectedContinuation.Decode<Payload>(protector, ProtectedContinuation.Encode(protector, input));
        Assert.Equal(input.Snapshot, decoded.Snapshot);
        Assert.Equal(input.Offset, decoded.Offset);
        Assert.Equal(input.Fields, decoded.Fields);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Offset\":")]
    public void Reject_Null_And_Malformed_JSON_Payloads(string json) {
        var protector = Protector();
        using var buffer = new MemoryStream();
        using (var compression = new BrotliStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) {
            compression.Write(Encoding.UTF8.GetBytes(json));
        }
        var token = WebEncoders.Base64UrlEncode(protector.Protect(buffer.ToArray()));
        Assert.Throws<JsonException>(() => ProtectedContinuation.Decode<Payload>(protector, token));
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("key-ring")]
    [InlineData("tamper")]
    public void Reject_Continuations_Outside_Their_Protection_Boundary(string changed) {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector("CodecTests");
        var token = ProtectedContinuation.Encode(protector, new Payload("reports/A/snapshots/daily", 3, ["value"]));
        if (changed == "purpose") protector = provider.CreateProtector("OtherPurpose");
        if (changed == "key-ring") protector = Protector();
        if (changed == "tamper") token = (token[0] == 'A' ? "B" : "A") + token[1..];
        Assert.Throws<CryptographicException>(() => ProtectedContinuation.Decode<Payload>(protector, token));
    }

    [Fact]
    public void Reject_Truncated_Continuations_On_Any_Target_Framework() {
        var protector = Protector();
        var token = ProtectedContinuation.Encode(protector, new Payload("reports/A/snapshots/daily", 3, ["value"]))[..^8];
        // Base64Url.DecodeFromChars rejects the truncated token with FormatException before unprotect runs on net10.0.
        var ex = Record.Exception(() => ProtectedContinuation.Decode<Payload>(protector, token));
        Assert.True(ex is FormatException or CryptographicException);
    }

    private static IDataProtector Protector() => new EphemeralDataProtectionProvider().CreateProtector("CodecTests");

    public sealed record Payload(string Snapshot, int Offset, string[] Fields);
}
