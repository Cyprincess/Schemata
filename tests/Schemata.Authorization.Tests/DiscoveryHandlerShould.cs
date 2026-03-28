using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class DiscoveryHandlerShould
{
    private const string Issuer = "https://auth.example.com";

    private static readonly RSA            Rsa       = RSA.Create(2048);
    private static readonly RsaSecurityKey SigningKey = new(Rsa);

    private static DiscoveryHandler<SchemataScope> CreateHandler(
        out DefaultHttpContext      httpContext,
        Action<IServiceCollection>? configure = null
    ) {
        var options = Options.Create(new SchemataAuthorizationOptions {
            Issuer           = Issuer,
            SigningKey       = SigningKey,
            SigningAlgorithm = SigningAlgorithms.RsaSha256,
        });

        var services = new ServiceCollection();
        services.AddSingleton(options);

        configure?.Invoke(services);

        var sp = services.BuildServiceProvider();

        httpContext                = new() { RequestServices = sp };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host   = new("auth.example.com");

        var scopesMock = new Mock<IScopeManager<SchemataScope>>();
        scopesMock.Setup(m => m.ListAsync(It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
                  .Returns(EmptyAsyncEnumerable<SchemataScope>());

        return new DiscoveryHandler<SchemataScope>(options, scopesMock.Object, sp);
    }

    private static DiscoveryDocument ExtractDocument(AuthorizationResult result) {
        Assert.Equal(AuthorizationStatus.Content, result.Status);
        return Assert.IsType<DiscoveryDocument>(result.Data);
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>([EnumeratorCancellation] CancellationToken ct = default) {
        await Task.CompletedTask;
        yield break;
    }

    [Fact]
    public async Task GetDiscovery_ComposesMultipleAdvisors() {
        var handler = CreateHandler(out var http, services => {
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryBase>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryClientCredentials>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryRefreshToken>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryIntrospection>());
        });

        var result   = await handler.GetDiscoveryDocumentAsync(Issuer, http.RequestAborted);
        var document = ExtractDocument(result);

        Assert.Equal($"{Issuer}/Connect/Token", document.TokenEndpoint);
        Assert.Equal($"{Issuer}/Connect/Introspect", document.IntrospectionEndpoint);
        Assert.NotNull(document.GrantTypesSupported);
        Assert.Contains("client_credentials", document.GrantTypesSupported);
        Assert.Contains("refresh_token", document.GrantTypesSupported);
    }

    [Fact]
    public void GetJwks_ReturnsPublicSigningKey() {
        var handler = CreateHandler(out _);
        var result  = handler.GetJwks();

        Assert.Equal(AuthorizationStatus.Content, result.Status);
        var jwks = Assert.IsType<Dictionary<string, object>>(result.Data);

        Assert.True(jwks.ContainsKey("keys"));
        var keys = (Dictionary<string, string?>[]?)jwks["keys"];
        Assert.NotNull(keys);
        Assert.Single(keys);

        var key = keys[0];
        Assert.Equal("RSA", key["kty"]);
        Assert.Equal("sig", key["use"]);
        Assert.Equal("RS256", key["alg"]);
        Assert.NotNull(key["n"]);
        Assert.NotNull(key["e"]);
    }
}
