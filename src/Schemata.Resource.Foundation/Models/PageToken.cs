using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Schemata.Resource.Foundation.Models;

public class PageToken
{
    public virtual string? Filter { get; set; }

    public virtual string? OrderBy { get; set; }

    public virtual bool? ShowDeleted { get; set; }

    public virtual int PageSize { get; set; }

    public virtual int Skip { get; set; }

    public async Task<string> ToStringAsync() {
        var json  = JsonSerializer.Serialize(this);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var       ms = new MemoryStream();
        await using var gz = new BrotliStream(ms, CompressionLevel.Optimal);
        gz.Write(bytes, 0, bytes.Length);
        gz.Close();

        return ms.ToArray().ToBase64UrlString();
    }

    public static async Task<PageToken?> FromStringAsync(string? token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        var bytes = token.FromBase64UrlString();

        using var       ms = new MemoryStream(bytes);
        await using var gz = new BrotliStream(ms, CompressionMode.Decompress);

        try {
            return await JsonSerializer.DeserializeAsync<PageToken>(gz);
        } catch {
            return null;
        }
    }
}
