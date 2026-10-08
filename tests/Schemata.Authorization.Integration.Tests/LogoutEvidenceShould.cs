using System;
using System.Threading;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class LogoutEvidenceShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Permanent_Participants_And_Expired_Credentials_Provide_Recipients_Without_Trust(bool linq) {
        await using var host = await TokenLifecycleShould.Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        await tokens.RegisterParticipantAsync("users/alice", "old", "applications/client");
        await tokens.CreateAsync(new() { Name = "expired", Parent = "users/alice", SessionId = "old", Application = "applications/client", Type = TokenTypes.AccessToken, Status = TokenStatuses.Valid, ExpireTime = DateTime.UtcNow.AddDays(-1) });
        var sessions = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), tokens: tokens);
        var target = new LogoutSessionTarget("users/alice", "old", "applications/client");
        Assert.Null(await sessions.ResolveLogoutAsync(target, User("old")));
        Assert.Equal("applications/client", Assert.Single(await tokens.ListParticipantsAsync("users/alice", "old")));
        await sessions.EstablishOnlineAsync("users/alice", "old");
        Assert.Null(await sessions.ResolveLogoutAsync(target, User("old")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_Live_Rp_Credential_And_Host_Or_Online_Authority_Prove_The_Target(bool linq) {
        await using var host = await TokenLifecycleShould.Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        await tokens.CreateAsync(new() { Name = "live", Parent = "users/alice", SessionId = "old", Application = "applications/client", Type = TokenTypes.AccessToken, Status = TokenStatuses.Valid, ExpireTime = DateTime.UtcNow.AddHours(1) });
        var sessions = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), tokens: tokens);
        var target = new LogoutSessionTarget("users/alice", "old", "applications/client");
        Assert.Equal(new LogoutSessionEvidence(target, true, false), await sessions.ResolveLogoutAsync(target, User("old")));
        Assert.Null(await sessions.ResolveLogoutAsync(target, User("new")));
        await sessions.EstablishOnlineAsync("users/alice", "old");
        Assert.Equal(new LogoutSessionEvidence(target, false, true), await sessions.ResolveLogoutAsync(target, null));
        Assert.Null(await sessions.ResolveLogoutAsync(target with { Application = "applications/other" }, User("old")));
        Assert.Null(await sessions.ResolveLogoutAsync(target with { Subject = "users/bob" }, User("old")));
        Assert.Null(await sessions.ResolveLogoutAsync(target with { SessionId = "new" }, User("new")));
        await sessions.InvalidateAsync(null, "users/alice", "old");
        Assert.Null(await sessions.ResolveLogoutAsync(target, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_One_Real_Confirmation_Redemption_Can_Authorize_Logout(bool linq) {
        await using var host = await TokenLifecycleShould.Host.CreateAsync(linq);
        using (var seed = host.Services.CreateScope()) {
            await seed.ServiceProvider.GetRequiredService<RepositoryTokenStore>().CreateAsync(new() {
                Name = "confirmation", ReferenceId = "confirmation", Type = TokenTypes.Logout,
                Status = TokenStatuses.Valid, Parent = "users/alice", SessionId = "sid-a",
            });
        }
        using var first = host.Services.CreateScope();
        using var second = host.Services.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        var b = second.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        var loadedA = (await a.FindByReferenceIdAsync("confirmation"))!;
        var loadedB = (await b.FindByReferenceIdAsync("confirmation"))!;
        Assert.True(await a.TryRedeemAsync(loadedA));
        Assert.False(await b.TryRedeemAsync(loadedB));
        using var verify = host.Services.CreateScope();
        Assert.Equal(TokenStatuses.Redeemed, (await verify.ServiceProvider.GetRequiredService<RepositoryTokenStore>()
            .FindByReferenceIdAsync("confirmation"))!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Notifier_And_Logout_Retirement_Use_The_Same_Exact_Canonical_Session(bool linq) {
        await using var host = await TokenLifecycleShould.Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        var apps = scope.ServiceProvider.GetRequiredService<Schemata.Authorization.Skeleton.Managers.IApplicationManager<Schemata.Authorization.Skeleton.Entities.SchemataApplication>>();
        await apps.CreateAsync(new() { Name = "client", ClientId = "client", FrontChannelLogoutUri = "https://rp.example/logout" });
        await tokens.RegisterParticipantAsync("users/alice", "old", "applications/client");
        await tokens.RegisterParticipantAsync("users/alice", "new", "applications/client");
        var options = Options.Create(new SchemataAuthorizationOptions { Issuer = "https://localhost" });
        var sessions = new DefaultOpSessionService(options, tokens: tokens);
        await sessions.EstablishOnlineAsync("users/alice", "old");
        await sessions.EstablishOnlineAsync("users/alice", "new");
        var notifier = new FrontChannelLogoutService<Schemata.Authorization.Skeleton.Entities.SchemataApplication>(apps, tokens, options);
        using var notificationServices = new ServiceCollection().AddSingleton<Schemata.Authorization.Skeleton.ILogoutNotifier>(notifier).BuildServiceProvider();
        var logout = new DefaultOpLogoutService(sessions, tokens, notificationServices,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultOpLogoutService>.Instance);
        var result = await logout.LogoutAsync(User("new"), "users/alice", "old");
        Assert.Equal("https://rp.example/logout?iss=https%3A%2F%2Flocalhost&sid=old", Assert.Single(result.FrontChannelUris));
        Assert.Empty(await tokens.ListParticipantsAsync("users/alice", "old"));
        Assert.Equal("applications/client", Assert.Single(await tokens.ListParticipantsAsync("users/alice", "new")));
        Assert.Null(await tokens.GetAsync("users/alice", "op-session", "old"));
        Assert.NotNull(await tokens.GetAsync("users/alice", "op-session", "new"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_Credential_Born_Under_An_Old_Generation_Cannot_Prove_Current_Logout(bool linq) {
        await using var host = await TokenLifecycleShould.Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        var sessions = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), tokens: tokens);
        var generation = await sessions.EstablishOnlineAsync("users/alice", "old");
        var grant = new AuthorizationGrantContext { NativeSessionKind = NativeSessionKinds.Online, OnlineSessionAuthority = generation };
        await tokens.CreateAsync(new() { Name = "native", Parent = "users/alice", SessionId = "old", Application = "applications/client",
            Type = TokenTypes.AccessToken, Status = TokenStatuses.Valid,
            GrantContext = System.Text.Json.JsonSerializer.Serialize(grant, Schemata.Common.SchemataJson.Default) });
        var target = new LogoutSessionTarget("users/alice", "old", "applications/client");
        Assert.True((await sessions.ResolveLogoutAsync(target, User("old")))!.MatchesCurrent);
        await sessions.InvalidateAsync(null, "users/alice", "old");
        Assert.NotEqual(generation, await sessions.EstablishOnlineAsync("users/alice", "old"));
        Assert.Null(await sessions.ResolveLogoutAsync(target, User("old")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Application_Adapter_Uses_Its_Real_Session_Retirement_And_Expiry(bool linq) {
        await using var host = await TokenLifecycleShould.Host.CreateAsync(linq);
        using var scope = host.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<RepositoryTokenStore>();
        var target = new LogoutSessionTarget("users/alice", "recent", "applications/client");
        var row = new SchemataToken { Name = "host-recent", ReferenceId = "host-recent", Parent = target.Subject,
            SessionId = target.SessionId, Application = target.Application, Status = TokenStatuses.Valid,
            Type = "host-session", ExpireTime = DateTime.UtcNow.AddDays(1) };
        await tokens.CreateAsync(row);
        var sessions = new DefaultOpSessionService(Options.Create(new SchemataAuthorizationOptions()), [new ApplicationSessions(tokens)]);
        Assert.Equal(new LogoutSessionEvidence(target, false, true), await sessions.ResolveLogoutAsync(target, null));
        row.ExpireTime = DateTime.UtcNow.AddDays(-1);
        await tokens.UpdateAsync(row);
        Assert.Null(await sessions.ResolveLogoutAsync(target, null));
        row.ExpireTime = DateTime.UtcNow.AddDays(1);
        await tokens.UpdateAsync(row);
        await tokens.RevokeAsync(row);
        Assert.Null(await sessions.ResolveLogoutAsync(target, null));
    }

    private sealed class ApplicationSessions(RepositoryTokenStore tokens) : IOpSessionStore
    {
        public int Order => 10;
        public Task<OpSessionEvidence?> ReadAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) => Task.FromResult<OpSessionEvidence?>(null);
        public async Task<LogoutSessionEvidence?> ReadLogoutAsync(LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
            await foreach (var row in tokens.ListBySessionAsync(target.SessionId, ct)) {
                if (row.Type == "host-session" && row.Parent == target.Subject && row.Application == target.Application
                    && row.ExpireTime > DateTime.UtcNow) return new(target, false, true);
            }
            return null;
        }
        public Task PersistAsync(string sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) => Task.CompletedTask;
        public async Task ClearAsync(string? sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
            await foreach (var row in tokens.ListBySessionAsync(sessionId, ct)) {
                if (row.Type == "host-session" && row.Parent == subject) await tokens.RevokeAsync(row, ct);
            }
        }
    }

    private static ClaimsPrincipal User(string sid) => new(new ClaimsIdentity([new("sub", "users/alice"), new("sid", sid)], "host"));
}
