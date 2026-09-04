using Schemata.Tenancy.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Tenancy.Foundation.Commands;
using Schemata.Tenancy.Foundation.Handlers;
using Schemata.Tenancy.Foundation.Queries;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class TenancyEntryEquivalenceShould
{
    [Fact]
    public async Task Facade_Dispatches_All_Nine_Verbs_With_Equivalent_Payloads() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Name = "acme" };
        var names  = new Dictionary<string, string?> { ["en"] = "Acme" };
        ImmutableArray<string> hosts = ["one.test", "two.test"];
        var requests     = new List<object>();
        var tokens       = new List<CancellationToken>();
        var dispatcher   = new Mock<IRequestDispatcher>(MockBehavior.Strict);
        SetupRequest<CreateTenantRequest<SchemataTenant>, Unit>(dispatcher, requests, tokens, Unit.Value);
        SetupRequest<UpdateTenantRequest<SchemataTenant>, Unit>(dispatcher, requests, tokens, Unit.Value);
        SetupRequest<DeleteTenantRequest<SchemataTenant>, Unit>(dispatcher, requests, tokens, Unit.Value);
        SetupRequest<SetTenantDisplayNameRequest<SchemataTenant>, Unit>(dispatcher, requests, tokens, Unit.Value);
        SetupRequest<SetTenantLocalizedDisplayNamesRequest<SchemataTenant>, Unit>(
            dispatcher, requests, tokens, Unit.Value);
        SetupRequest<SetTenantHostsRequest<SchemataTenant>, Unit>(dispatcher, requests, tokens, Unit.Value);
        SetupRequest<FindTenantByIdQuery<SchemataTenant>, SchemataTenant?>(
            dispatcher, requests, tokens, tenant);
        SetupRequest<FindTenantByHostQuery<SchemataTenant>, SchemataTenant?>(
            dispatcher, requests, tokens, tenant);
        SetupRequest<GetTenantHostsQuery<SchemataTenant>, ImmutableArray<string>>(
            dispatcher, requests, tokens, hosts);
        var manager = new SchemataTenantManager<SchemataTenant>(dispatcher.Object);
        using var source = new CancellationTokenSource();

        await manager.CreateAsync(tenant, source.Token);
        await manager.UpdateAsync(tenant, source.Token);
        await manager.DeleteAsync(tenant, source.Token);
        await manager.SetDisplayNameAsync(tenant, "Acme Europe", source.Token);
        await manager.SetDisplayNamesAsync(tenant, names, source.Token);
        await manager.SetHostsAsync(tenant, hosts, source.Token);
        var byId   = await manager.FindByTenantId(tenant.Uid, source.Token);
        var byHost = await manager.FindByHost("acme.test", source.Token);
        var foundHosts = await manager.GetHostsAsync(tenant, source.Token);

        Assert.Collection(
            requests,
            request => Assert.Same(tenant, Assert.IsType<CreateTenantRequest<SchemataTenant>>(request).Tenant),
            request => Assert.Same(tenant, Assert.IsType<UpdateTenantRequest<SchemataTenant>>(request).Tenant),
            request => Assert.Same(tenant, Assert.IsType<DeleteTenantRequest<SchemataTenant>>(request).Tenant),
            request => {
                var command = Assert.IsType<SetTenantDisplayNameRequest<SchemataTenant>>(request);
                Assert.Same(tenant, command.Tenant);
                Assert.Equal("Acme Europe", command.DisplayName);
            },
            request => {
                var command = Assert.IsType<SetTenantLocalizedDisplayNamesRequest<SchemataTenant>>(request);
                Assert.Same(tenant, command.Tenant);
                Assert.Same(names, command.DisplayNames);
            },
            request => {
                var command = Assert.IsType<SetTenantHostsRequest<SchemataTenant>>(request);
                Assert.Same(tenant, command.Tenant);
                Assert.Equal(hosts, command.Hosts);
            },
            request => Assert.Equal(
                tenant.Uid, Assert.IsType<FindTenantByIdQuery<SchemataTenant>>(request).TenantId),
            request => Assert.Equal(
                "acme.test", Assert.IsType<FindTenantByHostQuery<SchemataTenant>>(request).Host),
            request => Assert.Same(
                tenant, Assert.IsType<GetTenantHostsQuery<SchemataTenant>>(request).Tenant));
        Assert.All(tokens, token => Assert.Equal(source.Token, token));
        Assert.Same(tenant, byId);
        Assert.Same(tenant, byHost);
        Assert.Equal(hosts, foundHosts);
    }

    [Fact]
    public void Feature_Registers_All_Nine_Closed_Handlers() {
        var services = TenancyTestHost.CreateServices();

        AssertHandler<CreateTenantRequest<SchemataTenant>, Unit, CreateTenantHandler<SchemataTenant>>(services);
        AssertHandler<UpdateTenantRequest<SchemataTenant>, Unit, UpdateTenantHandler<SchemataTenant>>(services);
        AssertHandler<DeleteTenantRequest<SchemataTenant>, Unit, DeleteTenantHandler<SchemataTenant>>(services);
        AssertHandler<SetTenantDisplayNameRequest<SchemataTenant>, Unit,
            SetTenantDisplayNameHandler<SchemataTenant>>(services);
        AssertHandler<SetTenantLocalizedDisplayNamesRequest<SchemataTenant>, Unit,
            SetTenantLocalizedDisplayNamesHandler<SchemataTenant>>(services);
        AssertHandler<SetTenantHostsRequest<SchemataTenant>, Unit, SetTenantHostsHandler<SchemataTenant>>(services);
        AssertHandler<FindTenantByIdQuery<SchemataTenant>, SchemataTenant?,
            FindTenantByIdHandler<SchemataTenant>>(services);
        AssertHandler<FindTenantByHostQuery<SchemataTenant>, SchemataTenant?,
            FindTenantByHostHandler<SchemataTenant>>(services);
        AssertHandler<GetTenantHostsQuery<SchemataTenant>, ImmutableArray<string>,
            GetTenantHostsHandler<SchemataTenant>>(services);
    }

    [Fact]
    public void All_Nine_Contracts_Round_Trip_Through_Default_Json() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Name = "acme" };

        Assert.Equal("acme", RoundTrip(new CreateTenantRequest<SchemataTenant>(tenant)).Tenant.Name);
        Assert.Equal("acme", RoundTrip(new UpdateTenantRequest<SchemataTenant>(tenant)).Tenant.Name);
        Assert.Equal("acme", RoundTrip(new DeleteTenantRequest<SchemataTenant>(tenant)).Tenant.Name);
        Assert.Equal("Acme Europe", RoundTrip(
            new SetTenantDisplayNameRequest<SchemataTenant>(tenant, "Acme Europe")).DisplayName);
        Assert.Equal("Acme", RoundTrip(new SetTenantLocalizedDisplayNamesRequest<SchemataTenant>(
            tenant, new() { ["en"] = "Acme" })).DisplayNames["en"]);
        Assert.Equal(new[] { "one.test", "two.test" }, RoundTrip(
            new SetTenantHostsRequest<SchemataTenant>(tenant, ["one.test", "two.test"])).Hosts.ToArray());
        Assert.Equal(tenant.Uid, RoundTrip(new FindTenantByIdQuery<SchemataTenant>(tenant.Uid)).TenantId);
        Assert.Equal("acme.test", RoundTrip(new FindTenantByHostQuery<SchemataTenant>("acme.test")).Host);
        Assert.Equal("acme", RoundTrip(new GetTenantHostsQuery<SchemataTenant>(tenant)).Tenant.Name);
    }

    [Fact]
    public async Task Facade_And_Bare_Dispatcher_Run_Equivalent_Advisor_Chains() {
        var tenant  = new SchemataTenant { Uid = Guid.NewGuid(), Name = "acme" };
        var tenants = new Mock<IRepository<SchemataTenant>>();
        tenants.Setup(value => value.AddAsync(tenant, It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        tenants.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        tenants.Setup(value => value.SingleOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>>>(),
                          It.IsAny<CancellationToken>()))
               .ReturnsAsync(tenant);
        var commandAdvisor = new Mock<IRequestPipelineAdvisor<CreateTenantRequest<SchemataTenant>, Unit>>();
        commandAdvisor.SetupGet(value => value.Order).Returns(0);
        commandAdvisor.Setup(value => value.AdviseAsync(
                                 It.IsAny<AdviceContext>(),
                                 It.IsAny<CreateTenantRequest<SchemataTenant>>(),
                                 It.IsAny<RequestHandlerContinuation<Unit>>(),
                                 It.IsAny<CancellationToken>()))
                      .Returns((AdviceContext _, CreateTenantRequest<SchemataTenant> _, RequestHandlerContinuation<Unit> next, CancellationToken ct) => next(ct));
        var queryAdvisor = new Mock<IRequestPipelineAdvisor<FindTenantByIdQuery<SchemataTenant>, SchemataTenant?>>();
        queryAdvisor.SetupGet(value => value.Order).Returns(0);
        queryAdvisor.Setup(value => value.AdviseAsync(
                               It.IsAny<AdviceContext>(),
                               It.IsAny<FindTenantByIdQuery<SchemataTenant>>(),
                               It.IsAny<RequestHandlerContinuation<SchemataTenant?>>(),
                               It.IsAny<CancellationToken>()))
                    .Returns((AdviceContext _, FindTenantByIdQuery<SchemataTenant> _, RequestHandlerContinuation<SchemataTenant?> next, CancellationToken ct) => next(ct));
        using var provider = TenancyTestHost.CreateProvider(
            tenants,
            configure: services => {
                services.AddSingleton(commandAdvisor.Object);
                services.AddSingleton(queryAdvisor.Object);
            });
        var manager    = TenancyTestHost.Manager(provider);
        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        await manager.CreateAsync(tenant, CancellationToken.None);
        await dispatcher.SendAsync<CreateTenantRequest<SchemataTenant>, Unit>(
            new(tenant), CancellationToken.None);
        var facadeFound = await manager.FindByTenantId(tenant.Uid, CancellationToken.None);
        var directFound = await dispatcher.SendAsync<FindTenantByIdQuery<SchemataTenant>, SchemataTenant?>(
            new(tenant.Uid), CancellationToken.None);

        commandAdvisor.Verify(value => value.AdviseAsync(
                                  It.IsAny<AdviceContext>(),
                                  It.IsAny<CreateTenantRequest<SchemataTenant>>(),
                                  It.IsAny<RequestHandlerContinuation<Unit>>(),
                                  It.IsAny<CancellationToken>()), Times.Exactly(2));
        queryAdvisor.Verify(value => value.AdviseAsync(
                                It.IsAny<AdviceContext>(),
                                It.IsAny<FindTenantByIdQuery<SchemataTenant>>(),
                                It.IsAny<RequestHandlerContinuation<SchemataTenant?>>(),
                                It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Same(tenant, facadeFound);
        Assert.Same(facadeFound, directFound);
    }

    private static T RoundTrip<T>(T request) where T : class {
        var json = JsonSerializer.Serialize(request, SchemataJson.Default);
        Assert.False(string.IsNullOrWhiteSpace(json));
        return Assert.IsType<T>(JsonSerializer.Deserialize<T>(json, SchemataJson.Default));
    }

    private static void SetupRequest<TRequest, TResponse>(
        Mock<IRequestDispatcher> dispatcher,
        List<object>             requests,
        List<CancellationToken>  tokens,
        TResponse                response
    ) where TRequest : IRequest<TResponse> {
        dispatcher.Setup(value => value.SendAsync<TRequest, TResponse>(
                             It.IsAny<TRequest>(), It.IsAny<CancellationToken>()))
                  .Callback((TRequest request, CancellationToken ct) => {
                      requests.Add(request);
                      tokens.Add(ct);
                  })
                  .ReturnsAsync(response);
    }

    private static void AssertHandler<TRequest, TResponse, THandler>(IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : IRequestHandler<TRequest, TResponse> {
        var service = typeof(IRequestHandler<TRequest, TResponse>);
        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == service);
        Assert.Equal(typeof(THandler), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

}
