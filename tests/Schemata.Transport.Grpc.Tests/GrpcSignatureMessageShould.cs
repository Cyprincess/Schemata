using System;
using System.Collections.Generic;
using System.Net.Http;
using Grpc.Core;
using Moq;
using Schemata.Security.Foundation.Signatures;
using Schemata.Security.Skeleton.Services;
using Schemata.Transport.Grpc.Extensions;
using Xunit;

namespace Schemata.Transport.Grpc.Tests;

public class GrpcSignatureMessageShould
{
    private const string SharedSecret =
        "uzvJfB4u3N0Jy4T7NZ75MDVcr8zSTInedJtkgcu46YW4XByzNJjxBdtjUkdJPBtbmHhIDi6pcl8jsasjlTMtDQ==";

    private const string GrpcMethod = "/schemata.greet.v1.Greeter/Greet";

    [Fact]
    public void Produce_The_Same_Signature_On_Grpc_Metadata_And_Http_Headers() {
        var signer    = Signer();
        var key       = Key();
        var grpcView  = GrpcView();
        var httpView  = HttpView();

        var grpc = signer.Sign(grpcView, CoveredComponents(), key, Options());
        var http = signer.Sign(httpView, CoveredComponents(), key, Options());

        Assert.Equal(http.Input, grpc.Input);
        Assert.Equal(http.Value, grpc.Value);
    }

    [Fact]
    public void Verify_A_Signature_Across_Both_Transports() {
        var signer   = Signer();
        var verifier = Verifier();
        var key      = Key();

        var grpcSignature = signer.Sign(GrpcView(), CoveredComponents(), key, Options());
        var metadata      = GrpcMetadata();
        metadata.ApplySignature(grpcSignature);

        Assert.True(verifier.Verify(metadata.ToSignatureMessage(GrpcMethod, "example.com"), key));
        Assert.True(verifier.Verify(HttpRequest(grpcSignature).ToSignatureMessage(), key));

        var httpSignature = signer.Sign(HttpView(), CoveredComponents(), key, Options());
        Assert.True(verifier.Verify(GrpcView(httpSignature), key));
    }

    [Fact]
    public void Reject_A_Tampered_Covered_Metadata_Entry() {
        var signer   = Signer();
        var verifier = Verifier();
        var key      = Key();

        var signature = signer.Sign(GrpcView(), CoveredComponents(), key, Options());

        var metadata = new Metadata {
            { "content-type", "application/grpc" },
            { "x-request-id", "tampered" },
        };
        metadata.ApplySignature(signature);

        Assert.False(verifier.Verify(metadata.ToSignatureMessage(GrpcMethod, "example.com"), key));
    }

    [Fact]
    public void Exclude_Binary_Metadata_From_The_Message_View() {
        var metadata = new Metadata {
            { "content-type", "application/grpc" },
            { "payload-bin", new byte[] { 1, 2, 3 } },
        };

        var view = metadata.ToSignatureMessage(GrpcMethod, "example.com");

        Assert.Empty(view.GetFieldValues("payload-bin"));
        Assert.Equal(new[] { "application/grpc" }, view.GetFieldValues("content-type"));
    }

    [Fact]
    public void Keep_An_Ipv6_Literal_As_The_Authority_Without_Misparsing_A_Port() {
        var metadata = new Metadata();

        Assert.Equal("::1", metadata.ToSignatureMessage(GrpcMethod, "::1").Authority);
        Assert.Equal("[::1]", metadata.ToSignatureMessage(GrpcMethod, "[::1]:443").Authority);
        Assert.Equal("[::1]:8443", metadata.ToSignatureMessage(GrpcMethod, "[::1]:8443").Authority);
        Assert.Equal("example.com", metadata.ToSignatureMessage(GrpcMethod, "example.com:443").Authority);
    }

    private static HttpMessageSigner Signer() {
        return new(Clock().Object);
    }

    private static HttpMessageSignatureVerifier Verifier() {
        return new(Clock().Object);
    }

    private static Mock<TimeProvider> Clock() {
        var clock = new Mock<TimeProvider>();
        clock.Setup(provider => provider.GetUtcNow()).Returns(DateTimeOffset.FromUnixTimeSeconds(1618884473));
        return clock;
    }

    private static SecurityKeyMaterial Key() {
        return new SecurityKeyMaterial.Symmetric(Convert.FromBase64String(SharedSecret));
    }

    private static HttpMessageSignatureOptions Options() {
        return new() { KeyId = "test-shared-secret" };
    }

    private static List<MessageComponentIdentifier> CoveredComponents() {
        return [
            MessageComponentIdentifier.Parse("@method"),
            MessageComponentIdentifier.Parse("@path"),
            MessageComponentIdentifier.Parse("@authority"),
            MessageComponentIdentifier.Parse("content-type"),
            MessageComponentIdentifier.Parse("x-request-id"),
        ];
    }

    private static Metadata GrpcMetadata() {
        return new() {
            { "content-type", "application/grpc" },
            { "x-request-id", "abc-123" },
        };
    }

    private static SignatureMessage GrpcView(HttpMessageSignature? signature = null) {
        var metadata = GrpcMetadata();
        if (signature is not null) {
            metadata.ApplySignature(signature);
        }

        return metadata.ToSignatureMessage(GrpcMethod, "example.com");
    }

    private static HttpRequestMessage HttpRequest(HttpMessageSignature? signature = null) {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com" + GrpcMethod);
        request.Content = new StringContent("request");
        request.Content.Headers.ContentType = new("application/grpc");
        request.Headers.Add("x-request-id", "abc-123");
        if (signature is not null) {
            request.ApplySignature(signature);
        }

        return request;
    }

    private static SignatureMessage HttpView() {
        return HttpRequest().ToSignatureMessage();
    }
}
