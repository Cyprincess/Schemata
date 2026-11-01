using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Xunit;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Issue #131: custom client-authentication methods join the same
///     presentation-selection/assurance contract as the built-ins — their probe counts in
///     multiple-mechanism detection, their result obeys the effective registered method, and
///     assurance comes from their declared <c>VerifiesCredential</c>, never from inference.
/// </summary>
public class CustomClientAuthenticationShould
{
    private const string CustomMethod = "custom_mtls";

    private sealed class CustomAuthenticator(
        SchemataApplication app,
        bool                verifies
    ) : IClientAuthentication<SchemataApplication>
    {
        public string Method => CustomMethod;

        public bool VerifiesCredential => verifies;

        public bool Presents(
            Dictionary<string, List<string?>>? query,
            Dictionary<string, List<string?>>? form,
            Dictionary<string, List<string?>>? headers
        ) => headers is not null && headers.ContainsKey("X-Client-Cert");

        public Task<SchemataApplication?> AuthenticateAsync(
            Dictionary<string, List<string?>>? query,
            Dictionary<string, List<string?>>? form,
            Dictionary<string, List<string?>>? headers,
            CancellationToken                  ct,
            string?                             endpointAudience = null
        ) => Task.FromResult<SchemataApplication?>(app);
    }

    private static SchemataApplication App(string method = CustomMethod) {
        return new() {
            Uid                     = Guid.NewGuid(),
            ClientId                = "custom-client",
            CanonicalName           = "applications/custom-client",
            TokenEndpointAuthMethod = method,
        };
    }

    private static ClientAuthenticationService<SchemataApplication> Service(
        SchemataApplication          app,
        IClientAuthentication<SchemataApplication> custom
    ) {
        var manager = new Mock<IApplicationManager<SchemataApplication>>();
        manager.Setup(m => m.FindByClientIdAsync(app.ClientId!, It.IsAny<CancellationToken>()))
               .ReturnsAsync(app);
        var options = Microsoft.Extensions.Options.Options.Create(new Foundation.Authentication.SchemataAuthorizationOptions());
        return new([custom, new NoneAuthentication<SchemataApplication>(manager.Object, options)], new(), manager.Object);
    }

    private static Dictionary<string, List<string?>> CertificateHeader()
        => new() { ["X-Client-Cert"] = ["base64-der"] };

    [Fact]
    public async Task Presented_Custom_Method_Runs_Even_With_A_Client_Id() {
        var app     = App();
        var service = Service(app, new CustomAuthenticator(app, verifies: true));

        // A client_id on the wire must not divert the custom credential to the none method.
        var result = await service.AuthenticateAsync(
            null, ClientAuthenticationForm.Build("custom-client", null), CertificateHeader(), CancellationToken.None);

        Assert.Same(app, result?.Application);
        Assert.Equal(CustomMethod, result?.Method);
        Assert.True(result?.Authenticated);
    }

    [Fact]
    public async Task Custom_Identification_Level_Method_Is_Not_Authenticated() {
        var app     = App();
        var service = Service(app, new CustomAuthenticator(app, verifies: false));

        var result = await service.AuthenticateAsync(null, null, CertificateHeader(), CancellationToken.None);

        Assert.Same(app, result?.Application);
        Assert.False(result?.Authenticated);
    }

    [Fact]
    public async Task Custom_Method_Respects_The_Effective_Registered_Method() {
        // The app is registered for a built-in channel; the custom presentation is rejected as a
        // channel mismatch instead of being admitted by header-only claims.
        var app     = App(ClientAuthMethods.ClientSecretBasic);
        var service = Service(app, new CustomAuthenticator(app, verifies: true));

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.AuthenticateAsync(null, null, CertificateHeader(), CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        Assert.Equal(401,                       ex.Code);
    }

    [Fact]
    public async Task Custom_Credential_Beside_Basic_Is_A_Multiple_Mechanism_Error() {
        var app     = App();
        var service = Service(app, new CustomAuthenticator(app, verifies: true));

        var headers = CertificateHeader();
        headers["Authorization"] = ["Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("custom-client:secret"))];

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => service.AuthenticateAsync(null, ClientAuthenticationForm.Build("custom-client", null), headers, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public async Task Client_Id_Alone_Still_Routes_To_None_Not_The_Custom_Method() {
        var app     = App(ClientAuthMethods.None);
        var service = Service(app, new CustomAuthenticator(app, verifies: true));

        // No custom credential presented: identification alone is none's presentation.
        var result = await service.AuthenticateAsync(
            null, ClientAuthenticationForm.Build("custom-client", null), null, CancellationToken.None);

        Assert.Equal(ClientAuthMethods.None, result?.Method);
        Assert.False(result?.Authenticated);
    }
}
