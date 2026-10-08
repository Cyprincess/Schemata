using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Foundation.WebPush;

/// <summary>
///     Built-in Web Push delivery provider. Delivery is one POST per subscription endpoint
///     following RFC 8030 section 5 (TTL always sent per section 5.2; Urgency per section 5.3;
///     <see cref="PushOptions.CollapseKey" /> maps to the Topic replacement header per section 5.4);
///     payloads are encrypted per RFC 8291; requests self-identify with VAPID per RFC 8292.
/// </summary>
/// <remarks>
///     The provider keeps no delivery ledger and no retry state: a rejected send is reported as
///     <see cref="TransportStatus.Failed" /> and redelivery is an application concern expressed by
///     scheduling another send through the Push Scheduling bridge (<c>IScheduledPushService</c>).
///     Subscription rows are addressed with provider <see cref="PushConstants.WebPush.Provider" />,
///     the endpoint URL as the provider key, and the Push API keys in
///     <see cref="PushConstants.WebPush.PublicKeyMetadata" /> and
///     <see cref="PushConstants.WebPush.AuthSecretMetadata" />.
/// </remarks>
public sealed class WebPushTransport(
    IPushSubscriptionManager subscriptions,
    IHttpClientFactory       clients,
    IOptions<WebPushOptions> options,
    TimeProvider?            time = null
) : IPushTransport
{
    private static readonly JsonWebTokenHandler Tokens = new();

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string Name => PushConstants.WebPush.Provider;

    public async ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default) {
        if (context.Target is not RecipientTarget recipient) {
            return TransportResult.Skipped(Name);
        }

        var configuration = options.Value;

        ECDsa? signing;
        try {
            signing = LoadSigningKey(configuration);
        } catch (Exception exception) when (exception is FormatException or CryptographicException) {
            return TransportResult.Failed(Name, $"invalid VAPID key configuration: {exception.Message}");
        }

        if (signing is null) {
            return TransportResult.Failed(
                Name,
                "Web Push delivery requires VAPID keys: set WebPushOptions.VapidPublicKey and WebPushOptions.VapidPrivateKey");
        }

        using (signing) {
            if (configuration.TokenLifetime <= TimeSpan.Zero || configuration.TokenLifetime > WebPushOptions.MaxTokenLifetime) {
                return TransportResult.Failed(
                    Name,
                    $"WebPushOptions.TokenLifetime must be positive and at most 24 hours (RFC 8292 section 2); got {configuration.TokenLifetime}");
            }

            var ttl = context.Options.TimeToLive ?? configuration.DefaultTimeToLive;
            if (ttl < TimeSpan.Zero) {
                return TransportResult.Failed(Name, $"a negative TTL is not a valid RFC 8030 section 5.2 value; got {ttl}");
            }

            if (!IsValidTopic(context.Options.CollapseKey)) {
                return TransportResult.Failed(
                    Name,
                    "PushOptions.CollapseKey maps to the RFC 8030 section 5.4 Topic header: at most 32 characters from the URL-safe Base64 alphabet");
            }

            var payload = Serialize(context.Message);

            var sent    = 0;
            var failed  = 0;
            var error   = default(string);
            var address = default(string);
            var reference = default(string);
            await foreach (var subscription in subscriptions.GetForOwnerAsync(recipient.Subject, Name, ct)) {
                if (!Uri.TryCreate(subscription.ProviderKey, UriKind.Absolute, out var endpoint)) {
                    failed++;
                    error = "the subscription provider key is not an absolute push resource URL";
                    continue;
                }

                address = endpoint.GetLeftPart(UriPartial.Authority);
                try {
                    reference = await DeliverAsync(endpoint, subscription, payload, context.Options, ttl, signing, configuration, ct);
                    sent++;
                } catch (Exception exception) when (exception is not OperationCanceledException) {
                    failed++;
                    error = exception.Message;
                }
            }

            return (sent, failed) switch {
                (0, 0) => TransportResult.Skipped(Name),
                (_, 0) => TransportResult.Sent(Name, sent == 1 ? address : null, sent == 1 ? reference : null),
                (0, _) => TransportResult.Failed(Name, error!, address),
                _      => TransportResult.Failed(Name, $"delivered to {sent} endpoint(s); {failed} failed, last error: {error}", address),
            };
        }
    }

    private async Task<string?> DeliverAsync(
        Uri                    endpoint,
        SchemataPushSubscription subscription,
        byte[]                 payload,
        PushOptions            delivery,
        TimeSpan               ttl,
        ECDsa                  signing,
        WebPushOptions         configuration,
        CancellationToken      ct
    ) {
        if (subscription.Metadata is null
         || !subscription.Metadata.TryGetValue(PushConstants.WebPush.PublicKeyMetadata, out var publicKey) || string.IsNullOrWhiteSpace(publicKey)
         || !subscription.Metadata.TryGetValue(PushConstants.WebPush.AuthSecretMetadata, out var authSecret) || string.IsNullOrWhiteSpace(authSecret)) {
            throw new FormatException(
                $"the subscription lacks the {PushConstants.WebPush.PublicKeyMetadata}/{PushConstants.WebPush.AuthSecretMetadata} keys the Push API supplies");
        }

        var content = WebPushEncryption.Encrypt(payload, Base64UrlEncoder.DecodeBytes(publicKey), Base64UrlEncoder.DecodeBytes(authSecret));

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("TTL", ((long)ttl.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", UrgencyOf(delivery.Priority));
        if (!string.IsNullOrEmpty(delivery.CollapseKey)) {
            request.Headers.TryAddWithoutValidation("Topic", delivery.CollapseKey);
        }

        request.Headers.TryAddWithoutValidation("Authorization", CreateAuthorization(endpoint, signing, configuration));

        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");

        using var response = await clients.CreateClient(PushConstants.WebPush.HttpClientName).SendAsync(request, ct);
        if (response.IsSuccessStatusCode) {
            return response.Headers.Location?.ToString();
        }

        throw new HttpRequestException(response.StatusCode switch {
            HttpStatusCode.NotFound or HttpStatusCode.Gone => "the push service reports the subscription is expired or gone (RFC 8030 section 7.3)",
            HttpStatusCode.RequestEntityTooLarge         => "the push service rejected the payload as too large (RFC 8030 section 7.2)",
            (HttpStatusCode)429                          => "the push service throttled the request",
            HttpStatusCode.BadRequest                    => "the push service rejected the request as malformed",
            var status => $"the push service returned {(int)status} ({response.ReasonPhrase})",
        });
    }

    private string CreateAuthorization(Uri endpoint, ECDsa signing, WebPushOptions configuration) {
        var descriptor = new SecurityTokenDescriptor {
            Audience           = endpoint.GetLeftPart(UriPartial.Authority),
            Expires            = (_time.GetUtcNow() + configuration.TokenLifetime).UtcDateTime,
            SigningCredentials = new(new ECDsaSecurityKey(signing), SecurityAlgorithms.EcdsaSha256),
        };
        if (!string.IsNullOrWhiteSpace(configuration.Subject)) {
            descriptor.Claims = new Dictionary<string, object> { ["sub"] = configuration.Subject };
        }

        return $"vapid t={Tokens.CreateToken(descriptor)}, k={configuration.VapidPublicKey}";
    }

    private static ECDsa? LoadSigningKey(WebPushOptions configuration) {
        if (string.IsNullOrWhiteSpace(configuration.VapidPublicKey) || string.IsNullOrWhiteSpace(configuration.VapidPrivateKey)) {
            return null;
        }

        var publicKey = Base64UrlEncoder.DecodeBytes(configuration.VapidPublicKey);
        if (publicKey.Length != 65 || publicKey[0] != 0x04) {
            throw new FormatException("VapidPublicKey is a base64url-encoded 65-octet X9.62 uncompressed P-256 point");
        }

        return ECDsa.Create(new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = Base64UrlEncoder.DecodeBytes(configuration.VapidPrivateKey),
            Q = new() {
                X = publicKey[1..33],
                Y = publicKey[33..],
            },
        });
    }

    private static byte[] Serialize(object message) {
        return message switch {
            byte[] bytes       => bytes,
            string text        => Encoding.UTF8.GetBytes(text),
            JsonElement element => Encoding.UTF8.GetBytes(element.GetRawText()),
            _                  => JsonSerializer.SerializeToUtf8Bytes(message),
        };
    }

    private static string UrgencyOf(PushPriority priority) {
        return priority switch {
            PushPriority.Low               => "low",
            PushPriority.High              => "high",
            PushPriority.Urgent            => "high", // RFC 8030 section 5.3 tops out at "high"
            _                              => "normal",
        };
    }

    private static bool IsValidTopic(string? topic) {
        if (string.IsNullOrEmpty(topic)) {
            return true;
        }

        if (topic.Length > 32) {
            return false;
        }

        foreach (var c in topic) {
            var valid = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_';
            if (!valid) {
                return false;
            }
        }

        return true;
    }
}
