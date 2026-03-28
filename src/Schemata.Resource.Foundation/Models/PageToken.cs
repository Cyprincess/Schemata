using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Schemata.Resource.Foundation.Models;

/// <summary>
///     Represents a pagination cursor that encodes query parameters into a Brotli-compressed Base64 URL-safe token.
/// </summary>
public class PageToken
{
    /// <summary>
    ///     Gets or sets the filter expression carried across pages.
    /// </summary>
    public virtual string? Filter { get; set; }

    /// <summary>
    ///     Gets or sets the order-by clause carried across pages.
    /// </summary>
    public virtual string? OrderBy { get; set; }

    /// <summary>
    ///     Gets or sets the parent resource name for hierarchical listing.
    /// </summary>
    public virtual string? Parent { get; set; }

    /// <summary>
    ///     Gets or sets whether soft-deleted entities should be included.
    /// </summary>
    public virtual bool? ShowDeleted { get; set; }

    /// <summary>
    ///     Gets or sets the number of items per page (clamped to 1-100, default 25).
    /// </summary>
    public virtual int PageSize { get; set; }

    /// <summary>
    ///     Gets or sets the number of items to skip for the current page.
    /// </summary>
    public virtual int Skip { get; set; }

    /// <summary>
    ///     Serializes this token to a Brotli-compressed Base64 URL-safe string.
    /// </summary>
    /// <returns>The encoded page token string.</returns>
    public async Task<string> ToStringAsync() {
        var json  = JsonSerializer.Serialize(this);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var       ms = new MemoryStream();
        await using var gz = new BrotliStream(ms, CompressionLevel.Optimal);
        gz.Write(bytes, 0, bytes.Length);
        gz.Close();

        return ms.ToArray().ToBase64UrlString();
    }

    /// <summary>
    ///     Deserializes a page token from its Brotli-compressed Base64 URL-safe string representation.
    /// </summary>
    /// <param name="token">The encoded token string, or <see langword="null" />.</param>
    /// <returns>The deserialized page token, or <see langword="null" /> if the input is null/empty or invalid.</returns>
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
