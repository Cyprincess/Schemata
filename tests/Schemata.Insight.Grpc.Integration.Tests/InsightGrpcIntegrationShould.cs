using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Insight.Grpc.Integration.Tests.Fixtures;
using Schemata.Insight.Grpc.Wire;
using Schemata.Insight.Skeleton.Advisors;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Models;
using Xunit;

namespace Schemata.Insight.Grpc.Integration.Tests;

[Trait("Category", "Integration")]
public class InsightGrpcIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public InsightGrpcIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Query_ConfiguredScheme_FailsUnauthenticatedWithoutCredentials() {
        using var factory = _factory.WithAuthentication().WithServices(ConfigureAuthentication);

        var ex = await Assert.ThrowsAsync<RpcException>(() => Query(factory, new() {
            Sources = { new() { Alias = "b", Name = "buyers" } },
        }));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }

    [Fact]
    public async Task Query_ConfiguredScheme_PopulatesPrincipalForAuthenticatedRequest() {
        ClaimsPrincipal? principal = null;
        using var factory = _factory.WithAuthentication().WithServices(services => {
            ConfigureAuthentication(services);
            services.AddScoped<IInsightSourceAdvisor>(_ => new PrincipalProbeAdvisor(value => principal = value));
        });
        var invoker = factory.CreateGrpcChannel().CreateCallInvoker();
        using var call = invoker.AsyncUnaryCall(
            InsightGrpcMethods.Query,
            null,
            new(headers: new() { { "authorization", TestAuthenticationHandler.TestScheme } }),
            new() { Sources = { new() { Alias = "b", Name = "buyers" } } });

        var response = await call.ResponseAsync;

        Assert.Equal(2, response.Rows.Count);
        Assert.Equal("insight-test-user", principal?.Identity?.Name);
    }

    [Fact]
    public async Task Query_AllBuyers_ReturnsDynamicRows() {
        var response = await Query(_factory, new() {
            Sources = { new() { Alias = "b", Name = "buyers" } },
        });

        Assert.Equal(2, response.Rows.Count);
        Assert.Equal(2, response.TotalSize);
        Assert.Contains(response.Rows, row => row.Fields["full_name"].StringValue == "Ada");
        Assert.Contains(response.Rows, row => row.Fields["full_name"].StringValue == "Bob");
    }

    [Fact]
    public async Task Query_FilteredAndPaged_ProjectsAndPaginates() {
        var response = await Query(_factory, new() {
            Sources         = { new() { Alias = "b", Name = "buyers" } },
            Transformations = { new() { Filter = new() { Source = "id > 1", Language = "cel" } } },
            Selections      = { new() { Field = "b.full_name", Alias = "full_name" } },
            PageSize        = 1,
        });

        Assert.Single(response.Rows);
        Assert.Equal("Bob", response.Rows[0].Fields["full_name"].StringValue);
    }

    [Fact]
    public async Task Query_UnknownSource_FailsWithNotFound() {
        var ex = await Assert.ThrowsAsync<RpcException>(() => Query(_factory, new() {
            Sources = { new() { Alias = "x", Name = "missing" } },
        }));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    private static async Task<QueryInsightGrpcResponse> Query(WebAppFactory factory, QueryInsightGrpcRequest request) {
        var invoker = factory.CreateGrpcChannel().CreateCallInvoker();
        using var call = invoker.AsyncUnaryCall(InsightGrpcMethods.Query, null, new(), request);
        return await call.ResponseAsync;
    }

    private static void ConfigureAuthentication(IServiceCollection services) {
        services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.TestScheme, _ => { });
    }

    private sealed class PrincipalProbeAdvisor(Action<ClaimsPrincipal?> probe) : IInsightSourceAdvisor
    {
        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(
            AdviceContext       ctx,
            SourceBinding       binding,
            SourceConfig        config,
            ClaimsPrincipal?    principal,
            System.Threading.CancellationToken ct = default
        ) {
            probe(principal);
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory                               logger,
        UrlEncoder                                    encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "InsightTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (Request.Headers.Authorization != TestScheme) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, "insight-test-user")], TestScheme));
            return Task.FromResult(AuthenticateResult.Success(new(principal, TestScheme)));
        }
    }
}
