using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Caching.Skeleton;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public sealed class ExternalRequestObjectResolverShould
{
    [Theory]
    [InlineData("https://rp.example/other")]
    [InlineData("https://rp.example/request?version=2")]
    [InlineData("http://rp.example/request")]
    public async Task Require_An_Exact_Registered_Https_Uri(string candidate) {
        var handler = Handler(_ => Response("signed"));
        var resolver = Resolver(handler);
        var application = Application(candidate.StartsWith("http:", StringComparison.Ordinal)
            ? [candidate]
            : ["https://rp.example/request"]);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            resolver.ResolveAsync(application, new(candidate), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
        handler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Reject_A_Redirect_Response() {
        var handler = Handler(_ => new HttpResponseMessage(HttpStatusCode.Found) {
            Headers = { Location = new("https://rp.example/elsewhere") },
        });
        var resolver = Resolver(handler);
        var uri = new Uri("https://rp.example/request");

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            resolver.ResolveAsync(Application([uri.OriginalString]), uri, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
    }

    [Fact]
    public async Task Enforce_The_Configured_Timeout() {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
               .Returns(async (HttpRequestMessage _, CancellationToken ct) => {
                   await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                   return Response("unreachable");
               });
        var resolver = Resolver(handler, options => options.Timeout = TimeSpan.FromMilliseconds(20));
        var uri = new Uri("https://rp.example/request");

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            resolver.ResolveAsync(Application([uri.OriginalString]), uri, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enforce_The_Body_Limit_For_Declared_And_Streaming_Content(bool declareLength) {
        var handler = Handler(_ => {
            var response = Response("12345");
            if (!declareLength) {
                response.Content.Headers.ContentLength = null;
            }
            return response;
        });
        var resolver = Resolver(handler, options => options.MaximumBodyBytes = 4);
        var uri = new Uri("https://rp.example/request");

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            resolver.ResolveAsync(Application([uri.OriginalString]), uri, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
    }

    [Fact]
    public async Task Bind_Cached_Content_To_Metadata_Version_And_The_Complete_Uri() {
        var handler = Handler(request => Response(request.RequestUri?.Query == "?version=2" ? "second" : "first"));
        var resolver = Resolver(handler);
        var firstUri = Commit("https://rp.example/request?version=1", "first");
        var secondUri = Commit("https://rp.example/request?version=2", "second");
        var application = Application([firstUri]);

        Assert.Equal("first", await resolver.ResolveAsync(application, new(firstUri), CancellationToken.None));
        Assert.Equal("first", await resolver.ResolveAsync(application, new(firstUri), CancellationToken.None));
        VerifyRequests(handler, 1);

        application.Timestamp = Guid.NewGuid();
        Assert.Equal("first", await resolver.ResolveAsync(application, new(firstUri), CancellationToken.None));
        VerifyRequests(handler, 2);

        application.RequestUris = [secondUri];
        Assert.Equal("second", await resolver.ResolveAsync(application, new(secondUri), CancellationToken.None));
        VerifyRequests(handler, 3);
    }

    [Fact]
    public async Task Reject_Content_That_Does_Not_Match_The_Sha256_Fragment() {
        var uri = Commit("https://rp.example/request", "expected");
        var resolver = Resolver(Handler(_ => Response("different")));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            resolver.ResolveAsync(Application([uri]), new(uri), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("sha256=abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public async Task Reject_Malformed_Or_Unsupported_Fragment_Commitments(string fragment) {
        var uri = "https://rp.example/request#" + fragment;
        var handler = Handler(_ => Response("content"));
        var resolver = Resolver(handler);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            resolver.ResolveAsync(Application([uri]), new(uri), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestUri, exception.Status);
        VerifyRequests(handler, 0);
    }

    private static ExternalRequestObjectResolver Resolver(
        Mock<HttpMessageHandler> handler,
        Action<ExternalRequestObjectOptions>? configure = null
    ) {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(ExternalRequestObjectResolver.HttpClientName))
               .Returns(() => new HttpClient(handler.Object));
        var values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var cache = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) => values.GetValueOrDefault(key));
        cache.Setup(value => value.SetAsync(
                 It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] value, CacheEntryOptions _, CancellationToken _) => values[key] = value)
             .Returns(Task.CompletedTask);
        var options = new ExternalRequestObjectOptions();
        configure?.Invoke(options);
        return new(factory.Object, cache.Object, Options.Create(options));
    }

    private static Mock<HttpMessageHandler> Handler(Func<HttpRequestMessage, HttpResponseMessage> response) {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
               .ReturnsAsync((HttpRequestMessage request, CancellationToken _) => response(request));
        return handler;
    }

    private static void VerifyRequests(Mock<HttpMessageHandler> handler, int count) {
        handler.Protected().Verify(
            "SendAsync",
            Times.Exactly(count),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    private static HttpResponseMessage Response(string body) {
        return new(HttpStatusCode.OK) {
            Content = new StringContent(body, Encoding.UTF8, "application/oauth-authz-req+jwt"),
        };
    }

    private static SchemataApplication Application(ICollection<string> uris) {
        return new() {
            ClientId = "external-client",
            Timestamp = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RequestUris = uris,
        };
    }

    private static string Commit(string uri, string body) {
        return uri + "#" + Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }
}
