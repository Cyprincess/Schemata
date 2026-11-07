using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Caching.Skeleton;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Retrieves and caches external request objects within configured transport bounds.</summary>
public sealed class ExternalRequestObjectResolver(
    IHttpClientFactory http,
    ICacheProvider cache,
    IOptions<ExternalRequestObjectOptions> options
) : IExternalRequestObjectResolver
{
    public const string HttpClientName = "SchemataExternalRequestObjects";

    private static readonly UTF8Encoding Utf8 = new(false, true);

    public async Task<string> ResolveAsync(SchemataApplication application, Uri uri, CancellationToken ct) {
        if (!uri.IsAbsoluteUri
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
            || application.RequestUris?.Contains(uri.OriginalString, StringComparer.Ordinal) != true) {
            throw InvalidRequestUri();
        }

        byte[]? commitment;
        try {
            commitment = ReadCommitment(uri);
        } catch (FormatException) {
            throw InvalidRequestUri();
        }

        var key = string.Join('\x1e', "external-request-object", application.ClientId, application.Timestamp.ToString("N"), uri.OriginalString);
        var body = await cache.GetAsync(key, ct);
        var retrieved = body is null;
        body ??= await RetrieveAsync(uri, ct);

        if (commitment is not null) {
            var actual = SHA256.HashData(body);
            if (!CryptographicOperations.FixedTimeEquals(actual, commitment)) {
                throw InvalidRequestUri();
            }
        }

        if (retrieved) {
            await cache.SetAsync(key, body, new() {
                AbsoluteExpirationRelativeToNow = options.Value.CacheLifetime,
            }, ct);
        }

        try {
            return Utf8.GetString(body);
        } catch (DecoderFallbackException) {
            throw InvalidRequestUri();
        }
    }

    internal static byte[]? ReadCommitment(Uri uri) {
        var fragment = uri.GetComponents(UriComponents.Fragment, UriFormat.Unescaped);
        if (fragment.Length == 0) {
            return null;
        }
        if (fragment.Length != 43) {
            throw new FormatException("The request URI fragment is not a base64url SHA-256 commitment.");
        }

        var commitment = Base64UrlEncoder.DecodeBytes(fragment);
        if (commitment.Length != 32 || !string.Equals(Base64UrlEncoder.Encode(commitment), fragment, StringComparison.Ordinal)) {
            throw new FormatException("The request URI fragment is not a canonical SHA-256 commitment.");
        }
        return commitment;
    }

    private async Task<byte[]> RetrieveAsync(Uri uri, CancellationToken ct) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.Timeout);
        try {
            using var client = http.CreateClient(HttpClientName);
            var target = new UriBuilder(uri) { Fragment = string.Empty }.Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength > options.Value.MaximumBodyBytes) {
                throw InvalidRequestUri();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream(Math.Min(options.Value.MaximumBodyBytes, 8192));
            var buffer = new byte[Math.Min(options.Value.MaximumBodyBytes, 8192)];
            while (true) {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read == 0) {
                    break;
                }
                if (body.Length + read > options.Value.MaximumBodyBytes) {
                    throw InvalidRequestUri();
                }
                body.Write(buffer, 0, read);
            }
            return body.ToArray();
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw;
        } catch (OperationCanceledException) {
            throw InvalidRequestUri();
        } catch (Exception error) when (error is HttpRequestException or IOException) {
            throw InvalidRequestUri();
        }
    }

    private static OAuthException InvalidRequestUri() {
        return new(OAuthErrors.InvalidRequestUri, SchemataResources.INVALID_REQUEST_URI);
    }
}
