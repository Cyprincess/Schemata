using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Resource.Grpc;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class ApplicationLifecycleShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task NativeGrpcDelete_UsesApplicationManagerCascade() {
        using var host = factory.WithEnvironment("Grpc");
        using var http = host.CreateClient(new() { BaseAddress = new("http://localhost") });
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var configuration = ClientFactory.Create(host.Services.GetRequiredService<BinderConfiguration>());
        var client = channel.CreateGrpcService<IResourceService<SchemataApplication, ApplicationRequest, ApplicationDetail, ApplicationSummary>>(configuration);
        await client.DeleteAsync(new() { CanonicalName = "applications/test-client" });
        using var scope = host.Services.CreateScope();
        await using var verify = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>().CreateDbContextAsync();
        Assert.False(await verify.Applications.AnyAsync(a => a.ClientId == "test-client"));
        Assert.False(await verify.Securities.AnyAsync(s => s.Parent == "applications/test-client"));
        Assert.True(await verify.Securities.AnyAsync(s => s.Parent == "https://localhost"));
    }

    [Fact]
    public async Task NativeHttpDelete_UsesApplicationManagerCascade() {
        using var host = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();
        var response = await client.DeleteAsync("/v1/applications/test-client");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        using var scope = host.Services.CreateScope();
        await using var verify = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>().CreateDbContextAsync();
        Assert.False(await verify.Applications.AnyAsync(a => a.ClientId == "test-client"));
        Assert.False(await verify.Securities.AnyAsync(s => s.Parent == "applications/test-client"));
        Assert.True(await verify.Securities.AnyAsync(s => s.Parent == "https://localhost"));
    }

    [Fact]
    public async Task PublicationConflict_RollsBackManagerDeletionAndItsCascade() {
        using var host = factory.WithEnvironment("Testing");
        using var deletion = host.Services.CreateScope();
        var applications = deletion.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var application = new SchemataApplication { ClientId = "publication-wins" };
        await applications.CreateAsync(application);
        using (var publication = host.Services.CreateScope()) {
            var manager = publication.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
            var tokens = publication.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
            var token = new SchemataToken { Application = application.CanonicalName, Type = "access_token", Status = "valid" };
            await tokens.CreateAsync(token, default, (transaction, ct) => manager.EnlistTokenPublicationAsync(transaction, [token], ct));
        }
        await Assert.ThrowsAsync<AbortedException>(() => applications.DeleteAsync(application));
        await using var verify = await deletion.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>().CreateDbContextAsync();
        Assert.True(await verify.Applications.AnyAsync(a => a.Uid == application.Uid));
        Assert.Equal(1, await verify.Tokens.CountAsync(t => t.Application == application.CanonicalName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagerDeletion_PreventsPublicationBeforeAndAfterParentCheck(bool checkedBeforeDelete) {
        using var host = factory.WithEnvironment("Testing");
        using var publication = host.Services.CreateScope();
        var manager = publication.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var application = new SchemataApplication { ClientId = "deletion-wins" };
        await manager.CreateAsync(application);
        var tokens = publication.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        var token = new SchemataToken { Application = application.CanonicalName, Type = "access_token", Status = "valid" };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = tokens.CreateAsync(token, default, async (transaction, ct) => {
            if (checkedBeforeDelete) await manager.EnlistTokenPublicationAsync(transaction, [token], ct);
            entered.SetResult();
            await resume.Task;
            if (!checkedBeforeDelete) await manager.EnlistTokenPublicationAsync(transaction, [token], ct);
        });
        await entered.Task;
        try {
            using var deletion = host.Services.CreateScope();
            var deleting = deletion.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
            var current = await deleting.FindByClientIdAsync(application.ClientId);
            await deleting.DeleteAsync(current);
        } finally {
            resume.SetResult();
        }
        await Assert.ThrowsAsync<AbortedException>(async () => await pending);
        await using var verify = await publication.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>().CreateDbContextAsync();
        Assert.False(await verify.Applications.AnyAsync(a => a.Uid == application.Uid));
        Assert.False(await verify.Tokens.AnyAsync(t => t.Application == application.CanonicalName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FenceTokenOwner_FromParentOrAuthorizationReference(bool parentOnly) {
        using var host = factory.WithEnvironment("Testing");
        using var scope = host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var application = new SchemataApplication { ClientId = "indirect-owner" };
        await manager.CreateAsync(application);
        var grants = scope.ServiceProvider.GetRequiredService<IAuthorizationManager<SchemataAuthorization>>();
        var grant = new SchemataAuthorization { Application = application.CanonicalName };
        await grants.CreateAsync(grant);
        var current = (await manager.FindByClientIdAsync(application.ClientId))!;
        var originalStamp = current.Timestamp;
        var store = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        var token = new SchemataToken {
            Parent = parentOnly ? application.CanonicalName : null,
            Authorization = parentOnly ? null : grant.CanonicalName,
        };
        await store.CreateAsync(token, default, (transaction, ct) => manager.EnlistTokenPublicationAsync(transaction, [token], ct));
        await using var verify = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>().CreateDbContextAsync();
        Assert.NotEqual(originalStamp, (await verify.Applications.SingleAsync(a => a.Uid == application.Uid)).Timestamp);
        Assert.True(await verify.Tokens.AnyAsync(t => t.Uid == token.Uid));
        await Assert.ThrowsAsync<AbortedException>(() => manager.DeleteAsync(current));
    }

    [Fact]
    public async Task ReuseScopedManager_AfterLifecycleTransactions() {
        using var host = factory.WithEnvironment("Testing");
        using var scope = host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var first = new SchemataApplication { ClientId = "reuse-first" };
        var second = new SchemataApplication { ClientId = "reuse-second" };
        await manager.CreateAsync(first);
        await manager.DeleteAsync(first);
        await manager.CreateAsync(second);
        second.ClientUri = "https://updated.example";
        await manager.UpdateAsync(second);
        await manager.DeleteAsync(second);
        await using var verify = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>().CreateDbContextAsync();
        Assert.False(await verify.Applications.AnyAsync(a => a.ClientId == "reuse-first" || a.ClientId == "reuse-second"));
    }
}
