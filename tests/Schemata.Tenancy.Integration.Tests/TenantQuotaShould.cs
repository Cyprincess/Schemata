using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class TenantQuotaShould
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Bind_Fallback_Subject_Per_Rejection_And_Preserve_Policy_Metadata(bool metadata, bool anonymous) {
        using var services = Services(_ => { });
        var reject = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
        var lease = new Mock<RateLimitLease>();
        object? supplied = metadata ? "application:shared-quota" : null;
        lease.Setup(value => value.TryGetMetadata(QuotaMetadata.Subject.Name, out supplied)).Returns(metadata);
        object? retry = TimeSpan.FromSeconds(2);
        lease.Setup(value => value.TryGetMetadata(MetadataName.RetryAfter.Name, out retry)).Returns(true);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        if (!anonymous) http.User = new(new ClaimsIdentity([new("sub", "users/alice")], "test"));
        var caller = anonymous ? "client:127.0.0.1" : "users/alice";
        foreach (var uid in new Guid?[] {
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"), null,
        }) {
            using var frame = TenantContext.Enter(new(uid));
            var error = await Assert.ThrowsAsync<QuotaExceededException>(() => reject(new() { HttpContext = http, Lease = lease.Object }, default).AsTask());
            var subject = Assert.Single(Assert.Single(error.Details!.OfType<QuotaFailureDetail>()).Violations!).Subject;
            Assert.Equal(metadata ? "application:shared-quota" : uid is { } tenant ? $"tenants/{tenant:D}/{caller}" : caller, subject);
            Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(error.Details!.OfType<RetryInfoDetail>()).RetryDelay);
        }
    }

    [Fact]
    public async Task Preserve_Application_Exception_And_Snapshot_Issuing_Identity() {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var failure = new QuotaExceededException([new() { Subject = "users/alice", QuotaId = "application-quota" }]);
        using var services = Services(options => options.OnRejected = async (_, _) => {
            using var switched = TenantContext.Enter(new(second));
            await Task.Yield();
            throw failure;
        });
        using var frame = TenantContext.Enter(new(first));
        var reject = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
        var error = await Assert.ThrowsAsync<QuotaExceededException>(() => reject(new() { HttpContext = new DefaultHttpContext(), Lease = Mock.Of<RateLimitLease>() }, default).AsTask());
        Assert.Same(failure, error);
        var violation = Assert.Single(Assert.Single(error.Details!.OfType<QuotaFailureDetail>()).Violations!);
        Assert.Equal($"tenants/{first:D}/users/alice", violation.Subject);
        Assert.Equal("application-quota", violation.QuotaId);
    }

    [Fact]
    public async Task Preserve_Nonquota_Application_Failure() {
        var failure = new InvalidOperationException("policy-failure");
        using var services = Services(options => options.OnRejected = (_, _) => throw failure);
        using var frame = TenantContext.Enter(new(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        var reject = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => reject(new() { HttpContext = new DefaultHttpContext(), Lease = Mock.Of<RateLimitLease>() }, default).AsTask()));
    }

    [Fact]
    public async Task Preserve_Explicit_Application_Quota_Error_And_Lease_Subject() {
        var failure = new QuotaExceededException([new() { Subject = "application:quota", QuotaId = "application-limit" }]);
        using var services = Services(options => options.OnRejected = (_, _) => throw failure);
        var lease = new Mock<RateLimitLease>();
        object? supplied = "application:quota";
        lease.Setup(value => value.TryGetMetadata(QuotaMetadata.Subject.Name, out supplied)).Returns(true);
        using var frame = TenantContext.Enter(new(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        var reject = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
        var error = await Assert.ThrowsAsync<QuotaExceededException>(() => reject(new() { HttpContext = new DefaultHttpContext(), Lease = lease.Object }, default).AsTask());
        Assert.Same(failure, error);
        var violation = Assert.Single(Assert.Single(error.Details!.OfType<QuotaFailureDetail>()).Violations!);
        Assert.Equal("application:quota", violation.Subject);
        Assert.Equal("application-limit", violation.QuotaId);
    }

    [Fact]
    public async Task Preserve_Started_Application_Response_With_Installed_Tenancy() {
        var http = new DefaultHttpContext();
        var response = new Mock<IHttpResponseFeature>();
        response.SetupGet(value => value.HasStarted).Returns(true);
        http.Features.Set(response.Object);
        var called = false;
        using var services = Services(options => options.OnRejected = (_, _) => {
            called = true;
            return ValueTask.CompletedTask;
        });
        using var frame = TenantContext.Enter(new(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        await services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!(
            new() { HttpContext = http, Lease = Mock.Of<RateLimitLease>() }, default);
        Assert.True(called);
        response.VerifySet(value => value.StatusCode = It.IsAny<int>(), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_Application_Partition_Rejects_Second_Tenant_With_Current_Diagnostic(bool tenancy) {
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services => {
                services.AddSchemataJsonSerializer(_ => { });
                services.AddSchemataRateLimiter(options => options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                    _ => RateLimitPartition.GetFixedWindowLimiter("application-shared", _ => new() {
                        PermitLimit = 1, Window = TimeSpan.FromHours(1), QueueLimit = 0,
                    })));
                if (tenancy) new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
                    .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            }).Configure(app => {
                app.UseSchemataExceptionHandler();
                app.Use(async (http, next) => {
                    using var frame = TenantContext.Enter(new(Guid.Parse(http.Request.Headers["X-Tenant"].ToString())));
                    http.User = new(new ClaimsIdentity([new("sub", "users/alice")], "test"));
                    await next(http);
                });
                app.UseRateLimiter();
                app.Run(http => http.Response.WriteAsync("accepted"));
            })).StartAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant", "11111111-1111-1111-1111-111111111111");
        using var first = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Tenant");
        client.DefaultRequestHeaders.Add("X-Tenant", "22222222-2222-2222-2222-222222222222");
        using var rejected = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        var error = body.GetProperty("error");
        Assert.Equal("RESOURCE_EXHAUSTED", error.GetProperty("status").GetString());
        var quota = Assert.Single(error.GetProperty("details").EnumerateArray(), detail => detail.TryGetProperty("violations", out _));
        var subject = Assert.Single(quota.GetProperty("violations").EnumerateArray()).GetProperty("subject").GetString();
        Assert.Equal(tenancy ? "tenants/22222222-2222-2222-2222-222222222222/users/alice" : "users/alice", subject);
        await host.StopAsync();
    }

    private static ServiceProvider Services(Action<RateLimiterOptions> configure) {
        var services = new ServiceCollection().AddLogging().AddSchemataRateLimiter(configure);
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>, SchemataTenant, SchemataTenantHost>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        return services.BuildServiceProvider();
    }
}
