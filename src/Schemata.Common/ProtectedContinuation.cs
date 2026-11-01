using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Schemata.Common;

public static class ProtectedContinuation
{
    public static string Encode<T>(IDataProtector protector, T payload) {
        using var buffer = new MemoryStream();
        using (var compression = new BrotliStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) {
            JsonSerializer.Serialize(compression, payload, SchemataJson.Default);
        }

        return WebEncoders.Base64UrlEncode(protector.Protect(buffer.ToArray()));
    }

    public static T Decode<T>(IDataProtector protector, string token) {
        var bytes = protector.Unprotect(WebEncoders.Base64UrlDecode(token));
        using var buffer = new MemoryStream(bytes, writable: false);
        using var compression = new BrotliStream(buffer, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<T>(compression, SchemataJson.Default)
               ?? throw new JsonException("Continuation payload deserialized to null.");
    }
}
