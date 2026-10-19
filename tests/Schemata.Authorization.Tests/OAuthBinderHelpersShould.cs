using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Binding;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class OAuthBinderHelpersShould
{
    [Fact]
    public async Task Bind_PreservesWhitespaceOnlyChallenge_ForPkceGrammarRejection() {
        var request = new AuthorizeRequest();
        var property = typeof(AuthorizeRequest).GetProperty(nameof(AuthorizeRequest.CodeChallenge));
        Assert.NotNull(property);

        OAuthBinderHelpers.Bind(property, new("   "), request);

        var opts = new CodeFlowOptions { RequirePkce = false, RequirePkceS256 = true };
        var advisor = new AdviceAuthorizePkce<SchemataApplication>(Options.Create(opts));
        var authz = new AuthorizeContext<SchemataApplication> {
            Application = new() { ClientId = "test" },
            Request     = request,
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => advisor.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), authz));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
        Assert.Equal(SchemataResources.CODE_CHALLENGE_INVALID,
            Assert.Single(ex.Details!.OfType<ErrorInfoDetail>()).Reason);
    }

    [Fact]
    public void Bind_LeavesPropertyDefault_WhenParameterAbsentOrEmpty() {
        var request = new AuthorizeRequest();
        var property = typeof(AuthorizeRequest).GetProperty(nameof(AuthorizeRequest.CodeChallenge));
        Assert.NotNull(property);

        OAuthBinderHelpers.Bind(property, new(), request);
        Assert.Null(request.CodeChallenge);

        OAuthBinderHelpers.Bind(property, new(string.Empty), request);
        Assert.Null(request.CodeChallenge);
    }
}
