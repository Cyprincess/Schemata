using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class DiscoveryClientAuthShould
{
    // Mirrors the options wired by SchemataJsonSerializerFeature so assertions cover the real wire.
    private static readonly JsonSerializerOptions WireOptions = new() {
        DictionaryKeyPolicy    = JsonNamingPolicy.SnakeCaseLower,
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] SymmetricSigningAlgorithms =
        [SigningAlgorithms.HmacSha256, SigningAlgorithms.HmacSha384, SigningAlgorithms.HmacSha512];

    private static readonly string[] AsymmetricSigningAlgorithms = [
        SigningAlgorithms.RsaSha256,
        SigningAlgorithms.RsaSha384,
        SigningAlgorithms.RsaSha512,
        SigningAlgorithms.RsaPssSha256,
        SigningAlgorithms.RsaPssSha384,
        SigningAlgorithms.RsaPssSha512,
        SigningAlgorithms.EcdsaSha256,
        SigningAlgorithms.EcdsaSha384,
        SigningAlgorithms.EcdsaSha512,
    ];

    [Fact]
    public async Task Advertises_Secret_Methods_And_No_Assertion_Algorithms_By_Default() {
        var (document, wire) = await DiscoverAsync();

        AssertSet(
            [ClientAuthMethods.ClientSecretBasic, ClientAuthMethods.ClientSecretPost, ClientAuthMethods.None],
            document.TokenEndpointAuthMethodsSupported);
        Assert.False(wire.TryGetProperty("token_endpoint_auth_signing_alg_values_supported", out _));
    }

    [Fact]
    public async Task Advertises_Assertion_Methods_And_Unioned_Algorithms_When_Both_Assertion_Methods_Enabled() {
        var (document, wire) = await DiscoverAsync(options => {
            options.AllowedClientAuthMethods.Add(ClientAuthMethods.ClientSecretJwt);
            options.AllowedClientAuthMethods.Add(ClientAuthMethods.PrivateKeyJwt);
        });

        AssertSet(
            [
                ClientAuthMethods.ClientSecretBasic,
                ClientAuthMethods.ClientSecretPost,
                ClientAuthMethods.None,
                ClientAuthMethods.ClientSecretJwt,
                ClientAuthMethods.PrivateKeyJwt,
            ],
            document.TokenEndpointAuthMethodsSupported);
        AssertSet(
            [..SymmetricSigningAlgorithms, ..AsymmetricSigningAlgorithms],
            document.TokenEndpointAuthSigningAlgValuesSupported);
        Assert.Equal(
            12,
            wire.GetProperty("token_endpoint_auth_signing_alg_values_supported").GetArrayLength());
    }

    [Fact]
    public async Task Advertises_None_And_No_Assertion_Algorithms_When_None_Is_Allowed() {
        var (document, wire) = await DiscoverAsync(options => options.AllowedClientAuthMethods.Add(ClientAuthMethods.None));

        Assert.Contains(ClientAuthMethods.None, document.TokenEndpointAuthMethodsSupported!);
        Assert.False(wire.TryGetProperty("token_endpoint_auth_signing_alg_values_supported", out _));
    }

    [Fact]
    public async Task Advertises_Symmetric_Algorithms_Only_When_Only_Client_Secret_Jwt_Enabled() {
        var (document, _) = await DiscoverAsync(
            options => options.AllowedClientAuthMethods.Add(ClientAuthMethods.ClientSecretJwt));

        AssertSet(SymmetricSigningAlgorithms, document.TokenEndpointAuthSigningAlgValuesSupported);
    }

    [Fact]
    public async Task Advertises_Asymmetric_Algorithms_Only_When_Only_Private_Key_Jwt_Enabled() {
        var (document, _) = await DiscoverAsync(
            options => options.AllowedClientAuthMethods.Add(ClientAuthMethods.PrivateKeyJwt));

        AssertSet(AsymmetricSigningAlgorithms, document.TokenEndpointAuthSigningAlgValuesSupported);
    }

    [Fact]
    public async Task Oidc_True_With_Empty_Store_Throws_ServerError() {
        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBase>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryClientAuthentication<SchemataApplication>>());
        services.AddSingleton<IOptions<SchemataAuthorizationOptions>>(Options.Create(new SchemataAuthorizationOptions()));
        await using var provider = services.BuildServiceProvider();

        var scopes = new Mock<IScopeManager<SchemataScope>>();
        scopes.Setup(s => s.ListAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
              .Returns(ToAsync());

        var handler = new DiscoveryHandler<SchemataScope>(
            Options.Create(new SchemataAuthorizationOptions()),
            TestSecurityKeys.CreateTokenService(new() { Issuer = "https://op.example.com/connect" }, seed: false),
            scopes.Object);

        using var ambient = AdviceContext.Establish(new(provider));
        var ex = await Assert.ThrowsAsync<OAuthException>(() =>
            handler.GetDiscoveryDocumentAsync("https://op.example.com/connect", true, CancellationToken.None));
        Assert.Equal(OAuthErrors.ServerError, ex.Status);
    }

    [Fact]
    public async Task Oidc_False_With_Empty_Store_Returns_Document_Without_IdToken_Algorithms_Or_Scopes() {
        var (document, wire) = await DiscoverAsync(false);

        Assert.Null(document.IdTokenSigningAlgValuesSupported);
        Assert.Null(document.ScopesSupported);
        Assert.False(wire.TryGetProperty("id_token_signing_alg_values_supported", out _));
        Assert.False(wire.TryGetProperty("scopes_supported", out _));
    }

    [Fact]
    public async Task Require_Usable_Rs256_For_Oidc_Without_Imposing_It_On_OAuth() {
        var options = new SchemataAuthorizationOptions { Issuer = "https://op.example" };
        var service = TestSecurityKeys.CreateTokenService(options, algorithm: SecurityConstants.Algorithms.P256);
        var scopes = new Mock<IScopeManager<SchemataScope>>();
        scopes.Setup(s => s.ListAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).Returns(ToAsync());
        var handler = new DiscoveryHandler<SchemataScope>(Options.Create(options), service, scopes.Object);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var ambient = AdviceContext.Establish(new(provider));
        var error = await Assert.ThrowsAsync<OAuthException>(() => handler.GetDiscoveryDocumentAsync(options.Issuer, true, default));
        Assert.Equal(OAuthErrors.ServerError, error.Status);
        var oauth = await handler.GetDiscoveryDocumentAsync(options.Issuer, false, default);
        Assert.Equal(new[] { SigningAlgorithms.EcdsaSha256 }, Assert.IsType<DiscoveryDocument>(oauth.Data).IdTokenSigningAlgValuesSupported);
    }

    [Fact]
    public async Task Omit_Allowed_But_Uninstalled_Client_Authentication_Methods() {
        var options = new SchemataAuthorizationOptions();
        options.AllowedClientAuthMethods.Add(ClientAuthMethods.PrivateKeyJwt);
        var authenticator = new Mock<IClientAuthentication<SchemataApplication>>();
        authenticator.SetupGet(a => a.Method).Returns(ClientAuthMethods.None);
        var advisor = new AdviceDiscoveryClientAuthentication<SchemataApplication>(Options.Create(options), [authenticator.Object]);
        var discovery = new Schemata.Authorization.Skeleton.Contexts.DiscoveryContext { Document = new() };
        using var provider = new ServiceCollection().BuildServiceProvider();
        await advisor.AdviseAsync(new(provider), discovery);
        Assert.Equal(new[] { ClientAuthMethods.None }, discovery.Document.TokenEndpointAuthMethodsSupported);
        Assert.Null(discovery.Document.TokenEndpointAuthSigningAlgValuesSupported);
    }

    [Fact]
    public async Task Omit_Empty_Client_Authentication_Capabilities() {
        var advisor = new AdviceDiscoveryClientAuthentication<SchemataApplication>(Options.Create(new SchemataAuthorizationOptions()), []);
        var discovery = new Schemata.Authorization.Skeleton.Contexts.DiscoveryContext { Document = new() };
        using var provider = new ServiceCollection().BuildServiceProvider();
        await advisor.AdviseAsync(new(provider), discovery);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(discovery.Document, WireOptions));
        Assert.False(wire.RootElement.TryGetProperty("token_endpoint_auth_methods_supported", out _));
        Assert.False(wire.RootElement.TryGetProperty("token_endpoint_auth_signing_alg_values_supported", out _));
    }

    private static async Task<(DiscoveryDocument Document, JsonElement Wire)> DiscoverAsync(
        Action<SchemataAuthorizationOptions>? configure = null
    ) {
        return await DiscoverAsync(true, configure);
    }

    private static async Task<(DiscoveryDocument Document, JsonElement Wire)> DiscoverAsync(
        bool                                  oidc,
        Action<SchemataAuthorizationOptions>? configure = null
    ) {
        var options = new SchemataAuthorizationOptions { Issuer = "https://op.example.com/connect" };
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBase>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryClientAuthentication<SchemataApplication>>());
        services.AddSingleton<IOptions<SchemataAuthorizationOptions>>(Options.Create(options));
        foreach (var method in options.AllowedClientAuthMethods) {
            var authenticator = new Mock<IClientAuthentication<SchemataApplication>>();
            authenticator.SetupGet(a => a.Method).Returns(method);
            services.AddSingleton(authenticator.Object);
        }
        await using var provider = services.BuildServiceProvider();

        var scopes = new Mock<IScopeManager<SchemataScope>>();
        scopes.Setup(s => s.ListAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
              .Returns(ToAsync());

        var store = new TestSecurityStore();
        if (oidc) TestSecurityKeys.AddSigningRow(store, options.Issuer);

        var handler = new DiscoveryHandler<SchemataScope>(Options.Create(options), TestSecurityKeys.CreateTokenService(options, store, seed: false), scopes.Object);

        using var ambient = AdviceContext.Establish(new(provider));
        var       result  = await handler.GetDiscoveryDocumentAsync("https://op.example.com/connect", oidc, CancellationToken.None);

        var json = JsonSerializer.Serialize(result.Data, WireOptions);
        using var wire = JsonDocument.Parse(json);

        return (Assert.IsType<DiscoveryDocument>(result.Data), wire.RootElement.Clone());
    }

    private static async IAsyncEnumerable<SchemataScope> ToAsync(params SchemataScope[] rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private static void AssertSet(string[] expected, List<string>? actual) {
        Assert.NotNull(actual);
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            actual.Order(StringComparer.Ordinal));
    }
}
