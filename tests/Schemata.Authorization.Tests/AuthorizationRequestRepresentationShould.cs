using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Xunit;

namespace Schemata.Authorization.Tests;

public class AuthorizationRequestRepresentationShould
{
    [Fact]
    public async Task Reject_An_Unhandled_Request_Object() {
        var advisor = new AdviceAuthorizationRequestRepresentation<SchemataApplication>();
        var context = new AuthorizeContext<SchemataApplication> {
            Request = new() { Request = "request-object" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
                                                                     advisor.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), context));

        Assert.Equal(AuthorizationConstants.OAuthErrors.RequestNotSupported, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Unhandled_Request_Uri() {
        var advisor = new AdviceAuthorizationRequestRepresentation<SchemataApplication>();
        var context = new AuthorizeContext<SchemataApplication> {
            Request = new() { RequestUri = "urn:example:request" },
        };

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
                                                                     advisor.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), context));

        Assert.Equal(AuthorizationConstants.OAuthErrors.RequestUriNotSupported, exception.Status);
    }
}