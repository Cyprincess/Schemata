using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public sealed class RequestObjectMetadataShould
{
    [Fact]
    public async Task Apply_The_Oidc_Default_Content_Encryption_And_Echo_Negotiated_Metadata() {
        var options = Options();
        var advisor = new AdviceRegistrationJwtSecuredAuthorizationRequests<SchemataApplication>(Microsoft.Extensions.Options.Options.Create(options));
        var application = new SchemataApplication();
        var context = new AdviceContext(new Mock<IServiceProvider>().Object);

        var request = new RegisterRequest {
            GrantTypes = [GrantTypes.ClientCredentials], TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretBasic,
            RequestObjectEncryptionAlg = EncryptionAlgorithms.RsaOaep,
        };
        await advisor.AdviseAsync(context, request, application, CancellationToken.None);
        await Schemata.Authorization.Foundation.Services.RegistrationMetadataMapper.ApplyAsync(application, request,
            Microsoft.Extensions.Options.Options.Create(new Schemata.Authorization.Foundation.Authentication.SchemataAuthorizationOptions()),
            new Mock<System.Net.Http.IHttpClientFactory>().Object);
        var response = await Schemata.Authorization.Foundation.Services.RegistrationMetadataMapper.ToResponse(application,
            new Mock<Schemata.Authorization.Skeleton.Services.IApplicationSecurityMetadataStore>().Object);

        Assert.Equal(EncryptionAlgorithms.RsaOaep, application.RequestObjectEncryptionAlg);
        Assert.Equal(ContentEncryptionAlgorithms.Aes128CbcHmacSha256, application.RequestObjectEncryptionEnc);
        Assert.Equal(application.RequestObjectEncryptionAlg, response.RequestObjectEncryptionAlg);
        Assert.Equal(application.RequestObjectEncryptionEnc, response.RequestObjectEncryptionEnc);
    }

    [Theory]
    [InlineData(null, ContentEncryptionAlgorithms.Aes128CbcHmacSha256)]
    [InlineData(EncryptionAlgorithms.RsaOaep256, ContentEncryptionAlgorithms.Aes256CbcHmacSha512)]
    public async Task Reject_Unpaired_Or_Unsupported_Encryption_Metadata(string? alg, string enc) {
        var advisor = new AdviceRegistrationJwtSecuredAuthorizationRequests<SchemataApplication>(Microsoft.Extensions.Options.Options.Create(Options()));

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(
            new AdviceContext(new Mock<IServiceProvider>().Object),
            new RegisterRequest {
                RequestObjectEncryptionAlg = alg,
                RequestObjectEncryptionEnc = enc,
            },
            new SchemataApplication(),
            CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, exception.Status);
    }

    [Fact]
    public async Task Roundtrip_Exact_Registered_Request_Uris() {
        var uri = Commit("https://rp.example/request", "signed-request");
        var application = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
            new() {
                GrantTypes = [GrantTypes.ClientCredentials],
                RequestUris = [uri],
            },
            Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions()),
            HttpFactory(),
            CancellationToken.None);

        var security = new Mock<IApplicationSecurityMetadataStore>();
        security.Setup(value => value.ProjectAsync(
                    It.IsAny<SchemataApplication>(), It.IsAny<RegistrationResponse>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        var response = await RegistrationMetadataMapper.ToResponse(
            application,
            security.Object,
            CancellationToken.None);

        Assert.Equal([uri], application.RequestUris);
        Assert.Equal([uri], response.RequestUris);
    }

    [Theory]
    [InlineData("http://rp.example/request")]
    [InlineData("https://rp.example/request#short")]
    [InlineData("https://rp.example/request#sha256=abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ")]
    public async Task Reject_Unusable_Request_Uri_Metadata(string uri) {
        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
                new() {
                    GrantTypes = [GrantTypes.ClientCredentials],
                    RequestUris = [uri],
                },
                Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions()),
                HttpFactory(),
                CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, exception.Status);
    }

    private static JwtSecuredAuthorizationRequestsOptions Options() {
        var options = new JwtSecuredAuthorizationRequestsOptions();
        options.EncryptionAlgorithms.Add(EncryptionAlgorithms.RsaOaep);
        options.ContentEncryptionAlgorithms.Add(ContentEncryptionAlgorithms.Aes128CbcHmacSha256);
        options.ContentEncryptionAlgorithms.Add(ContentEncryptionAlgorithms.Aes256CbcHmacSha512);
        return options;
    }

    private static IHttpClientFactory HttpFactory() {
        return new Mock<IHttpClientFactory>().Object;
    }

    private static string Commit(string uri, string body) {
        return uri + "#" + Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }
}
