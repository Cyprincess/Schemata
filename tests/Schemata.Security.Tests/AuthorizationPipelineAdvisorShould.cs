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
using Xunit;namespace Schemata.Security.Tests;

public class AuthorizationPipelineAdvisorShould
{
    private readonly Mock<IPermissionMatcher>  _matcher  = new();
    private readonly Mock<IPermissionResolver> _resolver = new();

    [Fact]
    public void Expose_The_Authorization_Order() {
        var advisor = CreateAdvisor(_ => ResourceTarget.Collection(nameof(Operations.Get), typeof(Product)));

        Assert.Equal(SecurityOrders.Authorization, advisor.Order);
    }

    [Fact]
    public async Task Continue_When_The_Principal_Matches_The_Resolved_Permission() {
        var principal = AuthenticatedPrincipal();
        var request   = new TestRequest(principal);
        _resolver.Setup(value => value.Resolve(nameof(Operations.Update), typeof(Product))).Returns("product.update");
        _matcher.Setup(value => value.IsMatch(principal, "product.update")).Returns(true);
        var advisor = CreateAdvisor(_ => ResourceTarget.Collection(nameof(Operations.Update), typeof(Product)));
        var calls   = 0;

        var result = await advisor.AdviseAsync(Context(), request, Next, CancellationToken.None);

        Assert.Equal("completed", result);
        Assert.Equal(1, calls);
        _resolver.Verify(value => value.Resolve(nameof(Operations.Update), typeof(Product)), Times.Once);
        _matcher.Verify(value => value.IsMatch(principal, "product.update"), Times.Once);
        return;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }
    }

    [Theory]
    [InlineData(nameof(Operations.List))]
    [InlineData(nameof(Operations.Create))]
    [InlineData(nameof(Operations.Get))]
    [InlineData(nameof(Operations.Update))]
    [InlineData(nameof(Operations.Delete))]
    [InlineData("Approve")]
    public async Task Reject_Every_Denial_With_Permission_Denied_Without_Probing_Get(string operation) {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(operation, typeof(Product))).Returns("product.operation");
        _matcher.Setup(value => value.IsMatch(principal, "product.operation")).Returns(false);
        var advisor = CreateAdvisor(_ => ResourceTarget.Collection(operation, typeof(Product)));
        var calls   = 0;

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new(principal), Next, CancellationToken.None));

        Assert.Equal(403, exception.Code);
        Assert.Equal("PERMISSION_DENIED", exception.Status);
        Assert.Equal(0, calls);
        // AIP-211: authorization failures are PERMISSION_DENIED; a same-entity Get probe is not a
        // parent-resource check and must not run.
        _resolver.Verify(value => value.Resolve(operation, typeof(Product)), Times.Once);
        if (operation != nameof(Operations.Get)) {
            _resolver.Verify(value => value.Resolve(nameof(Operations.Get), typeof(Product)), Times.Never);
        }
        return;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }
    }

    [Fact]
    public async Task Continue_A_Matching_Permission_Once_With_Its_Cancellation_Token() {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(nameof(Operations.List), typeof(Product))).Returns("product.list");
        _matcher.Setup(value => value.IsMatch(principal, "product.list")).Returns(true);
        var       advisor      = CreateAdvisor(_ => ResourceTarget.Collection(nameof(Operations.List), typeof(Product)));
        using var cancellation = new CancellationTokenSource();
        var       calls        = 0;
        var       received     = default(CancellationToken);

        var result = await advisor.AdviseAsync(Context(), new(principal), Next, cancellation.Token);

        Assert.Equal("completed", result);
        Assert.Equal(1, calls);
        Assert.Equal(cancellation.Token, received);
        return;

        Task<string> Next(CancellationToken ct) {
            calls++;
            received = ct;
            return Task.FromResult("completed");
        }
    }

    [Fact]
    public async Task Bypass_Authorization_For_An_Anonymous_Entity_Operation() {
        var advisor = CreateAdvisor(_ => ResourceTarget.Collection(nameof(Operations.Create), typeof(PublicProduct)));

        var result = await advisor.AdviseAsync(Context(), new(null), _ => Task.FromResult("completed"), CancellationToken.None);

        Assert.Equal("completed", result);
        _resolver.Verify(value => value.Resolve(It.IsAny<string>(), It.IsAny<Type>()), Times.Never);
        _matcher.Verify(value => value.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Bypass_Authorization_When_The_Resolver_Has_No_Entity() {
        var advisor = CreateAdvisor(_ => new() { Operation = "Lookup" });

        var result = await advisor.AdviseAsync(Context(), new(null), _ => Task.FromResult("completed"), CancellationToken.None);

        Assert.Equal("completed", result);
        _resolver.Verify(value => value.Resolve(It.IsAny<string>(), It.IsAny<Type>()), Times.Never);
    }


    [Fact]
    public async Task Deny_With_The_Requested_Instance_Name_From_The_Typed_Target() {
        _resolver.Setup(value => value.Resolve(nameof(Operations.Get), typeof(Product))).Returns("product.get");
        _matcher.Setup(value => value.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.get")).Returns(false);
        var advisor = CreateAdvisor(_ => ResourceTarget.Instance(nameof(Operations.Get), typeof(Product), "products/p1"));

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new(AuthenticatedPrincipal()), _ => Task.FromResult("completed"), CancellationToken.None));

        Assert.Equal("PERMISSION_DENIED", exception.Status);
        var info = Assert.Single(exception.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
        Assert.Equal("products/p1", ((Abstractions.Errors.ResourceInfoDetail)info).ResourceName);
    }

    [Fact]
    public async Task Deny_A_Collection_Operation_Without_Fabricating_A_Name() {
        _resolver.Setup(value => value.Resolve(nameof(Operations.List), typeof(Product))).Returns("product.list");
        _matcher.Setup(value => value.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.list")).Returns(false);
        var advisor = CreateAdvisor(_ => ResourceTarget.Collection(nameof(Operations.List), typeof(Product)));

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new(AuthenticatedPrincipal()), _ => Task.FromResult("completed"), CancellationToken.None));

        Assert.Equal("PERMISSION_DENIED", exception.Status);
        var info = Assert.Single(exception.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
        Assert.Null(((Abstractions.Errors.ResourceInfoDetail)info).ResourceName);
    }

    [Fact]
    public async Task Reject_A_Null_Principal_Without_Calling_The_Matcher() {
        _resolver.Setup(value => value.Resolve(nameof(Operations.List), typeof(Product))).Returns("product.list");
        var advisor = CreateAdvisor(_ => ResourceTarget.Collection(nameof(Operations.List), typeof(Product)));
        var calls   = 0;

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new(null), Next, CancellationToken.None));

        Assert.Equal(403, exception.Code);
        Assert.Equal(0, calls);
        _matcher.Verify(value => value.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
        return;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }
    }

    private AuthorizationPipelineAdvisor<TestRequest, string> CreateAdvisor(Func<TestRequest, ResourceTarget> resolve) {
        return new(resolve, _resolver.Object, _matcher.Object, Mock.Of<Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider>());
    }

    private static ClaimsPrincipal AuthenticatedPrincipal() { return new(new ClaimsIdentity("test")); }

    private static AdviceContext Context() { return new(Mock.Of<IServiceProvider>()); }
}