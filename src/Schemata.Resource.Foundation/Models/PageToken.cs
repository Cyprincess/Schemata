using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.AspNetCore.DataProtection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Models;

/// <summary>
///     Encodes list query parameters (filter, order-by, parent, show-deleted, page size, skip)
///     into a Brotli-compressed, data-protected, Base64 URL-safe token
///     per <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso>. Tokens are
///     encrypted so clients cannot read or alter the continuation position; any token that
///     fails to decode raises <c>INVALID_ARGUMENT</c>.
/// </summary>
public class PageToken
{
    /// <summary>
    ///     The <see cref="IDataProtector" /> purpose string isolating page tokens
    ///     from other protected payloads.
    /// </summary>
    public const string ProtectionPurpose = "Schemata.Resource.Foundation.PageToken";

    /// <summary>
    ///     Gets or sets the filter expression.
    /// </summary>
    public virtual string? Filter { get; set; }

    /// <summary>
    ///     Gets or sets the filter expression language; a different language is a different page.
    /// </summary>
    public virtual string? Language { get; set; }

    /// <summary>
    ///     Gets or sets the order-by clause.
    /// </summary>
    public virtual string? OrderBy { get; set; }

    /// <summary>
    ///     Gets or sets the parent resource name for hierarchical listing.
    /// </summary>
    public virtual string? Parent { get; set; }

    /// <summary>
    ///     Gets or sets whether soft-deleted entities are included.
    /// </summary>
    public virtual bool? ShowDeleted { get; set; }

    /// <summary>
    ///     Gets or sets the number of items per page (clamped to 1–100, default 25).
    /// </summary>
    public virtual int PageSize { get; set; }

    /// <summary>
    ///     Gets or sets the skip offset for the current page.
    /// </summary>
    public virtual int Skip { get; set; }

    /// <summary>
    ///     Serializes this token to a Brotli-compressed, protected, Base64 URL-safe string.
    /// </summary>
    /// <param name="protector">The <see cref="IDataProtector" /> sealing the token.</param>
    /// <returns>The encoded page token.</returns>
    public async Task<string> ToStringAsync(IDataProtector protector) {
        var json  = JsonSerializer.Serialize(this);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var       ms = new MemoryStream();
        await using var gz = new BrotliStream(ms, CompressionLevel.Optimal);
        gz.Write(bytes, 0, bytes.Length);
        gz.Close();

        return protector.Protect(ms.ToArray()).ToBase64UrlString();
    }

    /// <summary>
    ///     Decodes a page token from its protected representation.
    ///     Returns <see langword="null" /> when the input is null or whitespace.
    /// </summary>
    /// <param name="token">The encoded token string, or <see langword="null" />.</param>
    /// <param name="protector">The <see cref="IDataProtector" /> that sealed the token.</param>
    /// <returns>The decoded token, or <see langword="null" /> for empty input.</returns>
    /// <exception cref="ValidationException">The token cannot be decoded.</exception>
    public static async Task<PageToken?> FromStringAsync(string? token, IDataProtector protector) {
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        try {
            var bytes = protector.Unprotect(token.FromBase64UrlString());

            using var       ms = new MemoryStream(bytes);
            await using var gz = new BrotliStream(ms, CompressionMode.Decompress);

            var parsed = await JsonSerializer.DeserializeAsync<PageToken>(gz);
            return parsed ?? throw new JsonException("Page token payload deserialized to null.");
        } catch (Exception ex) when (ex is FormatException
                                        or CryptographicException
                                        or JsonException
                                        or IOException
                                        or InvalidDataException) {
            throw new ValidationException([new() {
                Field       = nameof(ListRequest.PageToken).Underscore(),
                Description = SchemataResources.GetResourceString(SchemataResources.INVALID_PAGE_TOKEN),
                Reason      = SchemataResources.INVALID_PAGE_TOKEN,
            }]);
        }
    }
}
