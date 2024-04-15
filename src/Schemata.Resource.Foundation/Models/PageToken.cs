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

        return Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static async Task<PageToken?> FromStringAsync(string? token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        var base64 = token.Replace('_', '/').Replace('-', '+');
        switch (base64.Length % 4) {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        var bytes = Convert.FromBase64String(base64);

        using var       ms = new MemoryStream(bytes);
        await using var gz = new BrotliStream(ms, CompressionMode.Decompress);

        try {
            return await JsonSerializer.DeserializeAsync<PageToken>(gz);
        } catch {
            return null;
        }
    }
}
