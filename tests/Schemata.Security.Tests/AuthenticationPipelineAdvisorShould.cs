using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton.Advisors;
using Schemata.Security.Skeleton;
using Schemata.Security.Tests.Fixtures;
using Xunit;

namespace Schemata.Security.Tests;

public class AuthenticationPipelineAdvisorShould
{
    [Fact]
    public void Expose_The_Authentication_Order() {
        var advisor = new AuthenticationPipelineAdvisor<TestRequest, string>(_ => (nameof(Operations.Get), typeof(Product)));

        Assert.Equal(SecurityOrders.Authentication, advisor.Order);
    }

    [Fact]
    public async Task Continue_An_Authenticated_Request_Once_With_Its_Cancellation_Token() {
        var principal = AuthenticatedPrincipal();
        var request   = new TestRequest(principal);
        var advisor   = new AuthenticationPipelineAdvisor<TestRequest, string>(_ => (nameof(Operations.Get), typeof(Product)));
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var received = default(CancellationToken);

        Task<string> Next(CancellationToken ct) {
            calls++;
            received = ct;
            return Task.FromResult("completed");
        }

        var result = await advisor.AdviseAsync(Context(), request, Next, cancellation.Token);

        Assert.Equal("completed", result);
        Assert.Equal(1, calls);
        Assert.Equal(cancellation.Token, received);
    }

    [Fact]
    public async Task Reject_An_Unauthenticated_Request_Before_The_Continuation() {
        var advisor = new AuthenticationPipelineAdvisor<TestRequest, string>(_ => (nameof(Operations.Get), typeof(Product)));
        var calls = 0;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }

        var exception = await Assert.ThrowsAsync<UnauthenticatedException>(() =>
            advisor.AdviseAsync(Context(), new(null), Next, CancellationToken.None));

        Assert.Equal(401, exception.Code);
        Assert.Equal("UNAUTHENTICATED", exception.Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Bypass_Authentication_For_An_Anonymous_Entity_Operation() {
        var advisor = new AuthenticationPipelineAdvisor<TestRequest, string>(_ => (nameof(Operations.Create), typeof(PublicProduct)));
        var calls = 0;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }

        var result = await advisor.AdviseAsync(Context(), new(null), Next, CancellationToken.None);

        Assert.Equal("completed", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Resolve_The_Request_Before_Authenticating() {
        var principal = AuthenticatedPrincipal();
        var request   = new TestRequest(principal);
        var calls     = 0;
        TestRequest? resolved = null;
        var advisor = new AuthenticationPipelineAdvisor<TestRequest, string>(value => {
            calls++;
            resolved = value;
            return (nameof(Operations.Get), typeof(Product));
        });

        var result = await advisor.AdviseAsync(Context(), request, _ => Task.FromResult("completed"), CancellationToken.None);

        Assert.Equal("completed", result);
        Assert.Equal(1, calls);
        Assert.Same(request, resolved);
    }

    [Fact]
    public async Task Bypass_Authentication_When_The_Resolver_Has_No_Entity() {
        var advisor = new AuthenticationPipelineAdvisor<TestRequest, string>(_ => ("Lookup", null));

        var result = await advisor.AdviseAsync(Context(), new(null), _ => Task.FromResult("completed"), CancellationToken.None);

        Assert.Equal("completed", result);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal() { return new(new ClaimsIdentity("test")); }

    private static AdviceContext Context() { return new(Mock.Of<IServiceProvider>()); }
}