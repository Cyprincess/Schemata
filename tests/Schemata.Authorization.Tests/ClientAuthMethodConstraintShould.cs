using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ClientAuthMethodConstraintShould
{
    private static SchemataApplication CreateApp(string? method = null) {
        return new() {
            Uid                     = Guid.NewGuid(),
            ClientId                = "my-client",
            ClientType              = "confidential",
            TokenEndpointAuthMethod = method,
        };
    }

    private static Mock<IApplicationManager<SchemataApplication>> MockManager(SchemataApplication app) {
        var mock = new Mock<IApplicationManager<SchemataApplication>>();
        mock.Setup(m => m.FindByClientIdAsync(app.ClientId!, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        return mock;
    }
    private static (Mock<ISecurityStore<SchemataSecurity>> Securities, Mock<ISecretVerifier> Verifier) Credentials() {
        var securities = new Mock<ISecurityStore<SchemataSecurity>>();
        securities
            .Setup(s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Enumerate(new SchemataSecurity {
                Uid       = Guid.NewGuid(),
                Parent    = "applications/my-client",
                Kind      = SecurityConstants.Kinds.Password,
                Usage     = SecurityConstants.Usages.Authentication,
                Algorithm = SecurityConstants.Algorithms.Pbkdf2,
                Status    = SecurityConstants.Statuses.Valid,
                Value     = "stored-hash",
            }));

        var verifier = new Mock<ISecretVerifier>();
        verifier.Setup(v => v.VerifyAsync(It.IsAny<SchemataSecurity>(), "my-secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return (securities, verifier);
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(params SchemataSecurity[] rows) {
        foreach (var row in rows) {
            yield return row;
        }
    }

    private static Dictionary<string, List<string?>> BasicHeader(string value) {
        return new() { ["Authorization"] = [value] };
    }

    private static string Encode(string raw) { return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)); }

    private static ClientAuthenticationService<SchemataApplication> CreateService(
        Mock<IApplicationManager<SchemataApplication>> manager,
        params Mock<IClientAuthentication<SchemataApplication>>[] extra
    ) {
        var options                = Options.Create(new SchemataAuthorizationOptions());
        var (securities, verifier) = Credentials();
        var authenticators         = new List<IClientAuthentication<SchemataApplication>> {
            new ClientSecretBasicAuthentication<SchemataApplication>(manager.Object, options, securities.Object, verifier.Object),
            new ClientSecretPostAuthentication<SchemataApplication>(manager.Object, options, securities.Object, verifier.Object),
        };
        foreach (var mock in extra) {
            authenticators.Add(mock.Object);
        }

        return new(authenticators);
    }

    [Fact]
    public async Task Reject_Basic_For_Client_Registered_With_Private_Key_Jwt() {
        var app     = CreateApp(ClientAuthMethods.PrivateKeyJwt);
        var manager = MockManager(app);
        var service = CreateService(manager);
        var headers = BasicHeader(Encode("my-client:my-secret"));

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.AuthenticateAsync(null, null, headers, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        Assert.Equal(401,                       ex.Code);
        Assert.Equal(
            SchemataResources.GetResourceString(SchemataResources.UNAUTHORIZED_CLIENT_AUTH_METHOD), ex.Message);
    }

    [Fact]
    public async Task Allow_Basic_When_Token_Endpoint_Auth_Method_Is_Null() {
        var app     = CreateApp();
        var manager = MockManager(app);
        var service = CreateService(manager);
        var headers = BasicHeader(Encode("my-client:my-secret"));

        var result = await service.AuthenticateAsync(null, null, headers, CancellationToken.None);

        Assert.Same(app, result);
    }

    [Fact]
    public async Task Reject_Request_Presenting_Basic_And_Assertion_Together() {
        var app     = CreateApp();
        var manager = MockManager(app);

        var assertion = new Mock<IClientAuthentication<SchemataApplication>>();
        assertion.Setup(a => a.Method).Returns(ClientAuthMethods.PrivateKeyJwt);
        assertion
            .Setup(a => a.AuthenticateAsync(
                null,
                It.Is<Dictionary<string, List<string?>>?>(
                    form => form != null
                         && form.ContainsKey(Parameters.ClientAssertionType)
                         && form.ContainsKey(Parameters.ClientAssertion)),
                It.IsAny<Dictionary<string, List<string?>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(app);

        var service = CreateService(manager, assertion);
        var headers = BasicHeader(Encode("my-client:my-secret"));
        var form    = new Dictionary<string, List<string?>> {
            [Parameters.ClientAssertionType] = [ClientAssertionTypes.JwtBearer],
            [Parameters.ClientAssertion]     = ["jwt-assertion"],
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.AuthenticateAsync(null, form, headers, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        Assert.Equal(
            SchemataResources.GetResourceString(SchemataResources.MULTIPLE_CLIENT_AUTH_METHODS), ex.Message);
    }

    [Fact]
    public void Register_Assertion_Channel_Metadata_Constants() {
        Assert.Equal("client_secret_jwt", ClientAuthMethods.ClientSecretJwt);
        Assert.Equal("private_key_jwt",   ClientAuthMethods.PrivateKeyJwt);
        Assert.Equal("none",              ClientAuthMethods.None);

        Assert.Equal("client_assertion_type", Parameters.ClientAssertionType);
        Assert.Equal("client_assertion",      Parameters.ClientAssertion);

        Assert.Equal(
            "urn:ietf:params:oauth:client-assertion-type:jwt-bearer", ClientAssertionTypes.JwtBearer);
    }
}
