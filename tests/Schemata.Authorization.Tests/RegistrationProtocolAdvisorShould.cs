using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class RegistrationProtocolAdvisorShould
{
    [Fact]
    public async Task Project_Pushed_Authorization_Request_Metadata_Only_Through_Its_Advisor() {
        var advisor = new AdviceRegistrationPushedAuthorizationRequests<SchemataApplication>();
        var application = new SchemataApplication();
        var response = new RegistrationResponse();
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());

        await advisor.AdviseAsync(ctx, new RegisterRequest { RequirePushedAuthorizationRequests = true }, application);
        await advisor.AdviseAsync(ctx, application, response);

        Assert.True(application.RequirePushedAuthorizationRequests);
        Assert.True(response.RequirePushedAuthorizationRequests);
    }

    [Fact]
    public async Task Validate_And_Project_Jwt_Secured_Request_Metadata_Only_Through_Its_Advisor() {
        var options = new JwtSecuredAuthorizationRequestsOptions();
        options.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256);
        var advisor = new AdviceRegistrationJwtSecuredAuthorizationRequests<SchemataApplication>(Options.Create(options));
        var application = new SchemataApplication();
        var response = new RegistrationResponse();
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());

        await advisor.AdviseAsync(ctx, new RegisterRequest {
            RequestObjectSigningAlg = SigningAlgorithms.RsaSha256,
            RequireSignedRequestObject = true,
        }, application);
        await advisor.AdviseAsync(ctx, application, response);

        Assert.Equal(SigningAlgorithms.RsaSha256, application.RequestObjectSigningAlg);
        Assert.True(application.RequireSignedRequestObject);
        Assert.Equal(SigningAlgorithms.RsaSha256, response.RequestObjectSigningAlg);
        Assert.True(response.RequireSignedRequestObject);
    }

    [Fact]
    public async Task Reject_Unsupported_Jwt_Request_Metadata_Through_The_Protocol_Advisor() {
        var advisor = new AdviceRegistrationJwtSecuredAuthorizationRequests<SchemataApplication>(
            Options.Create(new JwtSecuredAuthorizationRequestsOptions()));
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());

        var exception = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(
            ctx,
            new RegisterRequest { RequestObjectSigningAlg = SigningAlgorithms.RsaSha256 },
            new SchemataApplication()));

        Assert.Equal(OAuthErrors.InvalidClientMetadata, exception.Status);
    }
}
