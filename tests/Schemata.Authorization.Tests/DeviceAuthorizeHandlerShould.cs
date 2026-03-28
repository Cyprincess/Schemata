using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class DeviceAuthorizeHandlerShould
{
    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static (DeviceAuthorizeHandler<SchemataApplication, SchemataToken> handler, Mock<ITokenManager<SchemataToken>> tokens) CreateHandler(
        SchemataApplication? application = null
    ) {
        var opts = new SchemataAuthorizationOptions();
        opts.AddEphemeralSigningKey();
        opts.Issuer                = "https://localhost";
        opts.DeviceVerificationUri = "https://localhost/device";
        opts.DeviceCodeLifetime    = TimeSpan.FromMinutes(15);
        opts.DeviceCodeInterval    = 5;

        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken t, CancellationToken _) => t);

        var jsonOpts = Options.Create(new JsonSerializerOptions());

        var services = new ServiceCollection();

        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();

        if (application is not null) {
            clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<Dictionary<string, List<string?>>?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(application);
        }

        var sp      = services.BuildServiceProvider();
        var handler = new DeviceAuthorizeHandler<SchemataApplication, SchemataToken>(clientAuth.Object, tokens.Object, Options.Create(opts), sp, jsonOpts);
        return (handler, tokens);
    }

    private static DeviceAuthorizeRequest CreateRequest(string? scope = null) {
        return new() { ClientId = "test-client", Scope = scope };
    }

    [Fact]
    public async Task ReturnsResponse_WithAllRequiredFields() {
        var app = new SchemataApplication { Id = 1, ClientId = "test-client" };
        var (handler, _) = CreateHandler(app);

        var result = await handler.DeviceAuthorizeAsync(CreateRequest("openid"), null, CancellationToken.None);

        Assert.Equal(AuthorizationStatus.Content, result.Status);
        var response = Assert.IsType<DeviceAuthorizationResponse>(result.Data);
        Assert.NotNull(response.DeviceCode);
        Assert.NotNull(response.UserCode);
        Assert.Equal("https://localhost/device", response.VerificationUri);
        Assert.NotNull(response.VerificationUriComplete);
        Assert.Contains("?user_code=", response.VerificationUriComplete);
        Assert.Equal(900, response.ExpiresIn);
        Assert.Equal(5, response.Interval);
    }

    [Fact]
    public async Task UserCode_HasCorrectFormat() {
        var app = new SchemataApplication { Id = 1, ClientId = "test-client" };
        var (handler, _) = CreateHandler(app);

        var result = await handler.DeviceAuthorizeAsync(CreateRequest(), null, CancellationToken.None);

        var response = Assert.IsType<DeviceAuthorizationResponse>(result.Data);
        var code     = response.UserCode!;

        Assert.Equal(9, code.Length);
        Assert.Equal('-', code[4]);

        foreach (var c in code) {
            if (c == '-') continue;
            Assert.Contains(c, UserCodeAlphabet);
        }
    }

    [Fact]
    public async Task CreatesDeviceCodeAndUserCodeTokens() {
        var app = new SchemataApplication { Id = 1, ClientId = "test-client" };
        var (handler, tokens) = CreateHandler(app);

        await handler.DeviceAuthorizeAsync(CreateRequest("openid"), null, CancellationToken.None);

        tokens.Verify(t => t.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
