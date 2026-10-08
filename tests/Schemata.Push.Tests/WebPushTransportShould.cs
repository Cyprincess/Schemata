using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Core;
using Schemata.Push.Foundation.Builders;
using Schemata.Push.Foundation.WebPush;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests;

[Trait("Layer", "Integration")]
public class WebPushTransportShould
{
    private const string Subject = "mailto:ops@example.com";

    [Fact]
    public async Task Deliver_Encrypted_And_Vapid_Signed_Message_To_The_Push_Service() {
        using var vapid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var vapidParameters = vapid.ExportParameters(true);
        var vapidPublic = UncompressedPoint(vapidParameters.Q);

        using var subscriber = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var subscriberParameters = subscriber.ExportParameters(true);
        var subscriberPublic = UncompressedPoint(subscriberParameters.Q);
        var authSecret = RandomNumberGenerator.GetBytes(16);

        await using var server = await LoopbackPushService.StartAsync();
        using var provider = BuildServices(
            VapidOptions(vapidPublic, vapidParameters.D!),
            Subscriptions(Subscription(server.Endpoint, Base64UrlEncoder.Encode(subscriberPublic), Base64UrlEncoder.Encode(authSecret))),
            out _);
        using var scope = provider.CreateScope();

        var transports = scope.ServiceProvider.GetServices<IPushTransport>().ToArray();
        Assert.Equal(2, transports.Length);
        var transport = Assert.IsType<WebPushTransport>(Assert.Single(transports, value => value.Name == PushConstants.WebPush.Provider));

        var before = DateTimeOffset.UtcNow;
        var result = await transport.TrySendAsync(new("hello web push", new RecipientTarget("users/one")) {
            Options = new() { Priority = PushPriority.High, TimeToLive = TimeSpan.FromSeconds(300), CollapseKey = "sync-1" },
        });

        Assert.Equal(PushConstants.WebPush.Provider, result.Transport);
        Assert.Equal(TransportStatus.Sent, result.Status);
        Assert.Equal("/messages/1", result.Provider);

        var request = await server.NextRequestAsync();
        Assert.Equal("300", request.Headers["TTL"]);
        Assert.Equal("high", request.Headers["Urgency"]);
        Assert.Equal("sync-1", request.Headers["Topic"]);
        Assert.Equal("aes128gcm", request.ContentEncoding.ToString());
        Assert.Equal("application/octet-stream", request.ContentType.ToString());

        VerifyVapidJwt(request.Headers["Authorization"], vapidPublic, server.Origin, before);

        var plaintext = Decrypt(request.Body, subscriberParameters.D!, subscriberPublic, authSecret);
        Assert.Equal("hello web push", Encoding.UTF8.GetString(plaintext));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Report_Expired_Subscription_As_Failed(int status) {
        using var vapid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var vapidParameters = vapid.ExportParameters(true);
        using var subscriber = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var subscriberParameters = subscriber.ExportParameters(true);

        await using var server = await LoopbackPushService.StartAsync(status);
        using var provider = BuildServices(
            VapidOptions(UncompressedPoint(vapidParameters.Q), vapidParameters.D!),
            Subscriptions(Subscription(
                server.Endpoint,
                Base64UrlEncoder.Encode(UncompressedPoint(subscriberParameters.Q)),
                Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16)))),
            out _);
        using var scope = provider.CreateScope();
        var transport = Assert.IsType<WebPushTransport>(scope.ServiceProvider.GetServices<IPushTransport>().Single(value => value.Name == PushConstants.WebPush.Provider));

        var result = await transport.TrySendAsync(new("gone", new RecipientTarget("users/one")));

        Assert.Equal(TransportStatus.Failed, result.Status);
        Assert.Contains("expired", result.Error);
    }

    [Fact]
    public async Task Skip_When_The_Recipient_Has_No_WebPush_Subscription() {
        using var vapid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var vapidParameters = vapid.ExportParameters(true);

        using var provider = BuildServices(VapidOptions(UncompressedPoint(vapidParameters.Q), vapidParameters.D!), Subscriptions(), out _);
        using var scope = provider.CreateScope();
        var transport = Assert.IsType<WebPushTransport>(scope.ServiceProvider.GetServices<IPushTransport>().Single(value => value.Name == PushConstants.WebPush.Provider));

        var result = await transport.TrySendAsync(new("hello", new RecipientTarget("users/nobody")));

        Assert.Equal(TransportStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task Skip_Non_Recipient_Targets() {
        using var vapid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var vapidParameters = vapid.ExportParameters(true);
        var subscriptions = Subscriptions();

        using var provider = BuildServices(VapidOptions(UncompressedPoint(vapidParameters.Q), vapidParameters.D!), subscriptions, out _);
        using var scope = provider.CreateScope();
        var transport = Assert.IsType<WebPushTransport>(scope.ServiceProvider.GetServices<IPushTransport>().Single(value => value.Name == PushConstants.WebPush.Provider));

        var result = await transport.TrySendAsync(new("hello", new ChannelTarget("general")));

        Assert.Equal(TransportStatus.Skipped, result.Status);
        subscriptions.Verify(
            value => value.GetForOwnerAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Refuse_Payloads_Beyond_The_Rfc8291_Single_Record_Limit() {
        using var vapid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var vapidParameters = vapid.ExportParameters(true);
        using var subscriber = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var subscriberParameters = subscriber.ExportParameters(true);

        await using var server = await LoopbackPushService.StartAsync();
        using var provider = BuildServices(
            VapidOptions(UncompressedPoint(vapidParameters.Q), vapidParameters.D!),
            Subscriptions(Subscription(
                server.Endpoint,
                Base64UrlEncoder.Encode(UncompressedPoint(subscriberParameters.Q)),
                Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16)))),
            out _);
        using var scope = provider.CreateScope();
        var transport = Assert.IsType<WebPushTransport>(scope.ServiceProvider.GetServices<IPushTransport>().Single(value => value.Name == PushConstants.WebPush.Provider));

        var result = await transport.TrySendAsync(new(new byte[WebPushEncryption.MaxPayloadLength + 1], new RecipientTarget("users/one")));

        Assert.Equal(TransportStatus.Failed, result.Status);
        Assert.Equal(0, server.Count);
    }

    [Fact]
    public async Task Report_Missing_Vapid_Keys_As_Failed() {
        using var provider = BuildServices(_ => { }, Subscriptions(), out _);
        using var scope = provider.CreateScope();
        var transport = Assert.IsType<WebPushTransport>(scope.ServiceProvider.GetServices<IPushTransport>().Single(value => value.Name == PushConstants.WebPush.Provider));

        var result = await transport.TrySendAsync(new("hello", new RecipientTarget("users/one")));

        Assert.Equal(TransportStatus.Failed, result.Status);
        Assert.Contains("VAPID", result.Error);
    }

    private static Action<WebPushOptions> VapidOptions(byte[] publicKey, byte[] privateKey) {
        return options => {
            options.Subject         = Subject;
            options.VapidPublicKey  = Base64UrlEncoder.Encode(publicKey);
            options.VapidPrivateKey = Base64UrlEncoder.Encode(privateKey);
        };
    }

    private static SchemataPushSubscription Subscription(Uri endpoint, string publicKey, string authSecret) {
        return new() {
            Provider    = PushConstants.WebPush.Provider,
            ProviderKey = endpoint.ToString(),
            Metadata = new() {
                [PushConstants.WebPush.PublicKeyMetadata]   = publicKey,
                [PushConstants.WebPush.AuthSecretMetadata] = authSecret,
            },
        };
    }

    private static Mock<IPushSubscriptionManager> Subscriptions(params SchemataPushSubscription[] rows) {
        var mock = new Mock<IPushSubscriptionManager>();
        mock.Setup(value => value.GetForOwnerAsync(It.IsAny<string>(), PushConstants.WebPush.Provider, It.IsAny<CancellationToken>()))
            .Returns(() => Enumerate());

        async IAsyncEnumerable<SchemataPushSubscription> Enumerate() {
            foreach (var row in rows) {
                yield return row;
            }
        }

        return mock;
    }

    private static ServiceProvider BuildServices(
        Action<WebPushOptions>          configure,
        Mock<IPushSubscriptionManager>  subscriptions,
        out Mock<IPushTransport>        other
    ) {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(subscriptions.Object);
        other = new Mock<IPushTransport>();
        services.AddSingleton(other.Object);
        new SchemataPushBuilder(new SchemataOptions(), services).AddWebPush(configure);
        return services.BuildServiceProvider();
    }

    private static byte[] UncompressedPoint(ECPoint point) {
        var result = new byte[65];
        result[0] = 0x04;
        point.X!.CopyTo(result, 1);
        point.Y!.CopyTo(result, 33);
        return result;
    }

    private static void VerifyVapidJwt(string authorization, byte[] expectedPublicKey, string expectedAudience, DateTimeOffset now) {
        Assert.StartsWith("vapid ", authorization);
        var parameters = authorization["vapid ".Length..].Split(", ");
        var token = Assert.Single(parameters, value => value.StartsWith("t=", StringComparison.Ordinal))[2..];
        var key   = Assert.Single(parameters, value => value.StartsWith("k=", StringComparison.Ordinal))[2..];
        Assert.Equal(Base64UrlEncoder.Encode(expectedPublicKey), key);

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());

        using var verifier = ECDsa.Create(new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new() {
                X = expectedPublicKey[1..33],
                Y = expectedPublicKey[33..],
            },
        });
        Assert.True(verifier.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64UrlEncoder.DecodeBytes(parts[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
        Assert.Equal(expectedAudience, payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal(Subject, payload.RootElement.GetProperty("sub").GetString());
        var expires = DateTimeOffset.FromUnixTimeSeconds(payload.RootElement.GetProperty("exp").GetInt64());
        Assert.InRange(expires, now, now + WebPushOptions.MaxTokenLifetime);
    }

    private static byte[] Decrypt(byte[] body, byte[] receiverPrivate, byte[] receiverPublic, byte[] authSecret) {
        var salt         = body[..16];
        var recordSize   = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4));
        Assert.Equal((uint)WebPushEncryption.RecordSize, recordSize);
        var senderPublic = body[21..86];
        var record       = body[86..];

        using var receiver = ECDiffieHellman.Create(new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            D     = receiverPrivate,
            Q = new() {
                X = receiverPublic[1..33],
                Y = receiverPublic[33..],
            },
        });
        using var sender = ECDiffieHellman.Create(new ECParameters {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new() {
                X = senderPublic[1..33],
                Y = senderPublic[33..],
            },
        });
        var shared = receiver.DeriveRawSecretAgreement(sender.PublicKey);

        // RFC 8291 section 3.3: key_info = "WebPush: info" || 0x00 || ua_public || as_public.
        var keyInfo = new byte[14 + 65 + 65];
        "WebPush: info\0"u8.CopyTo(keyInfo);
        receiverPublic.CopyTo(keyInfo, 14);
        senderPublic.CopyTo(keyInfo, 14 + 65);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, authSecret, keyInfo);

        var prk   = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek   = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        using var aes = new AesGcm(cek, 16);
        var padded = new byte[record.Length - 16];
        aes.Decrypt(nonce, record[..^16], record[^16..], padded);
        Assert.Equal(0x02, padded[^1]);
        return padded[..^1];
    }

    private sealed class LoopbackPushService : IAsyncDisposable
    {
        private readonly TaskCompletionSource<CapturedRequest> _captured = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebApplication _app = null!;
        private int _count;

        public Uri Endpoint { get; private set; } = null!;

        public string Origin => Endpoint.GetLeftPart(UriPartial.Authority);

        public int Count => _count;

        public sealed record CapturedRequest(IReadOnlyDictionary<string, string> Headers, byte[] Body)
        {
            public string ContentEncoding => Headers.GetValueOrDefault("Content-Encoding", "");

            public string ContentType => Headers.GetValueOrDefault("Content-Type", "");
        }

        public static async Task<LoopbackPushService> StartAsync(int status = 201) {
            var service = new LoopbackPushService();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = ["--urls", "http://127.0.0.1:0"] });
            service._app = builder.Build();
            service._app.MapPost("/{**path}", async context => {
                using var memory = new MemoryStream();
                await context.Request.Body.CopyToAsync(memory);
                Interlocked.Increment(ref service._count);
                service._captured.TrySetResult(new(
                    context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                    memory.ToArray()));
                context.Response.StatusCode = status;
                if (status == 201) {
                    context.Response.Headers.Location = "/messages/1";
                }
            });
            await service._app.StartAsync();
            service.Endpoint = new Uri($"{service._app.Urls.Single()}/push/abc");
            return service;
        }

        public async Task<CapturedRequest> NextRequestAsync() {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await _captured.Task.WaitAsync(cts.Token);
        }

        public async ValueTask DisposeAsync() {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
