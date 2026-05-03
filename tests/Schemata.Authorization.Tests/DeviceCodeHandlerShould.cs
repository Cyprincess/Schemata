using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class DeviceCodeHandlerShould
{
    private static Fixture CreateFixture(string? approvedScope = "openid profile email") {
        var jsonOpts = Options.Create(new JsonSerializerOptions());

        var app        = new SchemataApplication { Id = 1, Name = "test-app", ClientId = "test-client" };
        var clientAuth = new Mock<IClientAuthenticationService<SchemataApplication>>();
        clientAuth.Setup(c => c.AuthenticateAsync(It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<Dictionary<string, List<string?>>?>(),
                                                  It.IsAny<CancellationToken>()))
                  .ReturnsAsync(app);

        var payload = JsonSerializer.Serialize(
            new DeviceCodePayload { Scope = approvedScope, ClientId = app.ClientId }, jsonOpts.Value);

        var device = new SchemataToken {
            Id                = 1,
            Name              = "device-1",
            Type              = TokenTypes.DeviceCode,
            Status            = TokenStatuses.Authorized,
            ApplicationName   = app.Name,
            Subject           = "user-1",
            ReferenceId       = "dev-ref",
            Payload           = payload,
            AuthorizationName = "auth-approved",
            SessionId         = "sess-approved",
            ExpireTime        = DateTime.UtcNow.AddMinutes(10),
        };

        var tokens = new Mock<ITokenManager<SchemataToken>>();
        tokens.Setup(t => t.FindByReferenceIdAsync("dev-ref", It.IsAny<CancellationToken>())).ReturnsAsync(device);

        var sp = new ServiceCollection().BuildServiceProvider();
        var handler = new DeviceCodeHandler<SchemataApplication, SchemataToken>(
            clientAuth.Object, tokens.Object, sp, jsonOpts);

        return new(handler, tokens, device, app);
    }

    private static TokenRequest CreateRequest(string? scope = null, string deviceCode = "dev-ref") {
        return new() {
            GrantType  = GrantTypes.DeviceCode,
            ClientId   = "test-client",
            DeviceCode = deviceCode,
            Scope      = scope,
        };
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenDeviceCodeEmpty() {
        var f       = CreateFixture();
        var request = CreateRequest(deviceCode: string.Empty);

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenDeviceCodeNotFound() {
        var f       = CreateFixture();
        var request = CreateRequest(deviceCode: "missing");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    [Fact]
    public async Task UsesApprovedScope_WhenRequestScopeOmitted() {
        var f       = CreateFixture();
        var request = CreateRequest();

        var result = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.NotNull(result.Properties);
        Assert.Equal("openid profile email", result.Properties![Properties.Scope]);
    }

    [Fact]
    public async Task UsesRequestScope_WhenNarrowerThanApproved() {
        var f       = CreateFixture();
        var request = CreateRequest("profile");

        var result = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Equal("profile", result.Properties![Properties.Scope]);
    }

    [Fact]
    public async Task UsesRequestScope_WhenEqualToApproved() {
        var f       = CreateFixture("openid profile");
        var request = CreateRequest("openid profile");

        var result = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Equal("openid profile", result.Properties![Properties.Scope]);
    }

    [Fact]
    public async Task ThrowsInvalidScope_WhenRequestScopeIntroducesNewScope() {
        var f       = CreateFixture("openid profile");
        var request = CreateRequest("openid profile email");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidScope, ex.Code);
    }

    [Fact]
    public async Task ThrowsInvalidScope_WhenNoScopeApprovedButClientRequestsOne() {
        var f       = CreateFixture(null);
        var request = CreateRequest("openid");

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              request, null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidScope, ex.Code);
    }

    [Fact]
    public async Task PropagatesAuthorizationNameAndSessionId_OnSuccessfulExchange() {
        var f       = CreateFixture();
        var request = CreateRequest();

        var result = await f.Handler.HandleAsync(request, null, CancellationToken.None);

        Assert.Equal("auth-approved", result.Properties![Properties.AuthorizationName]);
        Assert.Equal("sess-approved", result.Properties![Properties.SessionId]);
    }

    [Fact]
    public async Task ThrowsInvalidGrant_WhenPayloadMissing() {
        var f = CreateFixture();
        f.Device.Payload = null;

        var ex = await Assert.ThrowsAsync<OAuthException>(() => f.Handler.HandleAsync(
                                                              CreateRequest(), null, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidGrant, ex.Code);
    }

    #region Nested type: Fixture

    private record Fixture(
        DeviceCodeHandler<SchemataApplication, SchemataToken> Handler,
        Mock<ITokenManager<SchemataToken>>                    Tokens,
        SchemataToken                                         Device,
        SchemataApplication                                   App
    );

    #endregion
}
