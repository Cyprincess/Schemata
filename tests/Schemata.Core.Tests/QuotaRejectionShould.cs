using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;
using Xunit;

namespace Schemata.Core.Tests;

public class QuotaRejectionShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Prefer_Policy_Subject_And_Preserve_Lease_Retry(bool metadata) {
        var lease = new Mock<RateLimitLease>();
        object? subject = metadata ? "tenant:approved/subject:42" : null;
        lease.Setup(l => l.TryGetMetadata(QuotaMetadata.Subject.Name, out subject)).Returns(metadata);
        object? retry = TimeSpan.FromMilliseconds(1250);
        lease.Setup(l => l.TryGetMetadata(MetadataName.RetryAfter.Name, out retry)).Returns(true);
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new("sub", "users/42")], "test")) };
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        var tenant = Guid.NewGuid();
        using var entered = TenantContext.Enter(new(tenant));
        using var services = new ServiceCollection().AddLogging().AddSchemataRateLimiter(_ => { }).BuildServiceProvider();
        var rejected = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
        var error = await Assert.ThrowsAsync<QuotaExceededException>(() => rejected(new() { HttpContext = http, Lease = lease.Object }, default).AsTask());
        var quota = Assert.Single(error.Details!.OfType<QuotaFailureDetail>());
        Assert.Equal(metadata ? "tenant:approved/subject:42" : $"tenants/{tenant:D}/users/42", Assert.Single(quota.Violations!).Subject);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), Assert.Single(error.Details!.OfType<RetryInfoDetail>()).RetryDelay);
    }

    [Fact]
    public async Task Ignore_Subject_Claims_On_Unauthenticated_Identities() {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new[] {
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "users/authenticated")], "test"),
            new ClaimsIdentity([new Claim("sub", "users/untrusted")]),
        }) };
        using var services = new ServiceCollection().AddLogging().AddSchemataRateLimiter(_ => { }).BuildServiceProvider();
        var reject = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
        var error = await Assert.ThrowsAsync<QuotaExceededException>(() => reject(new() {
            HttpContext = http, Lease = Mock.Of<RateLimitLease>(),
        }, default).AsTask());
        Assert.Equal("users/authenticated", Assert.Single(Assert.Single(error.Details!.OfType<QuotaFailureDetail>()).Violations!).Subject);
    }

    [Fact]
    public async Task Fall_Back_To_Ip_When_Authenticated_Claims_Are_Blank() {
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new("sub", " ")], "test")) };
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        using var services = new ServiceCollection().AddLogging().AddSchemataRateLimiter(_ => { }).BuildServiceProvider();
        var reject = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
        var error = await Assert.ThrowsAsync<QuotaExceededException>(() => reject(new() { HttpContext = http, Lease = Mock.Of<RateLimitLease>() }, default).AsTask());
        Assert.Equal("client:127.0.0.1", Assert.Single(Assert.Single(error.Details!.OfType<QuotaFailureDetail>()).Violations!).Subject);
    }
    [Fact]
    public async Task Preserve_Application_Started_Response() {

        var http = new DefaultHttpContext();
        var response = new Mock<IHttpResponseFeature>();
        response.SetupGet(r => r.HasStarted).Returns(true);
        http.Features.Set(response.Object);
        var called = false;
        using var services = new ServiceCollection().AddLogging().AddSchemataRateLimiter(options => {
            options.OnRejected = (_, _) => { called = true; return ValueTask.CompletedTask; };
        }).BuildServiceProvider();
        await services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!(new() { HttpContext = http, Lease = Mock.Of<RateLimitLease>() }, CancellationToken.None);
        Assert.True(called);
        response.VerifySet(r => r.StatusCode = It.IsAny<int>(), Times.Never);
    }
}
