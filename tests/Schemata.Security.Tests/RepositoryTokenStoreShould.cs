using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Tests;

public class RepositoryTokenStoreShould
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_Twice_Through_One_Injected_Repository() {
        var (store, repository) = NewStore();

        await store.CreateAsync(new() { Name = "access" });
        await store.CreateAsync(new() { Name = "refresh" });

        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Update_After_Create_Through_One_Injected_Repository() {
        var (store, repository) = NewStore();
        var token = new SchemataToken { Name = "access" };

        await store.CreateAsync(token);
        await store.UpdateAsync(token);

        repository.Verify(r => r.UpdateAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Revoke_By_Authorization_Flips_Only_NonRevoked_Matching_Rows_And_Commits_Once() {
        var revoked   = new SchemataToken { Name = "revoked",   Authorization = "auth-1", Status = Statuses.Revoked };
        var refreshed = new SchemataToken { Name = "refreshed", Authorization = "auth-1", Status = Statuses.Valid };
        var code      = new SchemataToken { Name = "code",      Authorization = "auth-1", Status = Statuses.Valid };
        var other     = new SchemataToken { Name = "other",     Authorization = "auth-2", Status = Statuses.Valid };
        var (store, repository) = NewStore(r => SetupList(r, revoked, refreshed, code, other));

        var count = await store.RevokeByAuthorizationAsync("auth-1");

        Assert.Equal(2, count);
        Assert.Equal(Statuses.Revoked, refreshed.Status);
        Assert.Equal(Statuses.Revoked, code.Status);
        Assert.Equal(Statuses.Valid,   other.Status);
        repository.Verify(r => r.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_By_Session_Flips_Only_NonRevoked_Matching_Rows_And_Commits_Once() {
        var live  = new SchemataToken { Name = "live",  SessionId = "sid-1", Status = Statuses.Valid };
        var gone  = new SchemataToken { Name = "gone",  SessionId = "sid-1", Status = Statuses.Revoked };
        var other = new SchemataToken { Name = "other", SessionId = "sid-2", Status = Statuses.Valid };
        var (store, repository) = NewStore(r => SetupList(r, live, gone, other));

        var count = await store.RevokeBySessionAsync("sid-1");

        Assert.Equal(1, count);
        Assert.Equal(Statuses.Revoked, live.Status);
        Assert.Equal(Statuses.Valid,   other.Status);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_Marks_The_Token_And_Commits() {
        var (store, repository) = NewStore();
        var token = new SchemataToken { Name = "access", Status = Statuses.Valid };

        await store.RevokeAsync(token);

        Assert.Equal(Statuses.Revoked, token.Status);
        repository.Verify(r => r.UpdateAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryRedeem_Transitions_A_Valid_Token_To_Redeemed_And_Returns_True() {
        var (store, repository) = NewStore();
        var token = new SchemataToken { Name = "code", Status = Statuses.Valid };

        var redeemed = await store.TryRedeemAsync(token);

        Assert.True(redeemed);
        Assert.Equal(Statuses.Redeemed, token.Status);
        repository.Verify(r => r.UpdateAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryRedeem_Returns_False_When_The_Concurrency_Cas_Aborts() {
        var (store, repository) = NewStore(r => r.Setup(x => x.UpdateAsync(
                        It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new AbortedException()));
        var token = new SchemataToken { Name = "code", Status = Statuses.Valid };

        var redeemed = await store.TryRedeemAsync(token);

        Assert.False(redeemed);
        repository.Verify(
            r => r.UpdateAsync(
                It.Is<SchemataToken>(t => ReferenceEquals(t, token) && t.Status == Statuses.Redeemed),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Prune_Removes_Rows_Expired_Before_Or_Revoked_At_Now() {
        var expired = new SchemataToken { Name = "expired", ExpireTime = Now.AddSeconds(-1) };
        var revoked = new SchemataToken { Name = "revoked", Status = Statuses.Revoked };
        var live    = new SchemataToken { Name = "live",    Status = Statuses.Valid, ExpireTime = Now.AddMinutes(5) };
        var (store, repository) = NewStore(r => SetupList(r, expired, revoked, live), NewClock());

        var count = await store.PruneAsync();

        Assert.Equal(2, count);
        repository.Verify(r => r.RemoveAsync(expired, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RemoveAsync(revoked, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RemoveAsync(live, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Find_By_A_Blank_Name_Returns_Null() {
        var (store, _) = NewStore();

        Assert.Null(await store.FindByNameAsync(null));
        Assert.Null(await store.FindByNameAsync("  "));
    }

    [Fact]
    public async Task Find_By_A_Blank_Reference_Returns_Null() {
        var (store, _) = NewStore();

        Assert.Null(await store.FindByReferenceIdAsync(null));
        Assert.Null(await store.FindByReferenceIdAsync("  "));
    }

    [Fact]
    public async Task Find_Matches_Only_The_Row_Carrying_The_Reference() {
        var match = new SchemataToken { Name = "access", ReferenceId = "ref-1" };
        var other = new SchemataToken { Name = "code",   ReferenceId = "ref-2" };
        var (store, _) = NewStore(r => SetupSingle(r, match, other));

        Assert.Same(match, await store.FindByReferenceIdAsync("ref-1"));
    }

    [Fact]
    public async Task List_By_Parent_Filters_Valid_Rows_And_Optional_Type() {
        var access  = new SchemataToken { Name = "access",  Parent = "users/u-1", Type = "access_token",  Status = Statuses.Valid };
        var refresh = new SchemataToken { Name = "refresh", Parent = "users/u-1", Type = "refresh_token", Status = Statuses.Valid };
        var revoked = new SchemataToken { Name = "revoked", Parent = "users/u-1", Type = "access_token",  Status = Statuses.Revoked };
        var other   = new SchemataToken { Name = "other",   Parent = "users/u-2", Type = "access_token",  Status = Statuses.Valid };
        var (store, _) = NewStore(r => SetupList(r, access, refresh, revoked, other));

        var found = new List<SchemataToken>();
        await foreach (var token in store.ListByParentAsync("users/u-1", "access_token")) {
            found.Add(token);
        }

        Assert.Same(access, Assert.Single(found));
    }

    [Fact]
    public async Task List_By_Session_Returns_Only_Valid_Session_Rows() {
        var live    = new SchemataToken { Name = "live",    SessionId = "sid-1", Status = Statuses.Valid };
        var revoked = new SchemataToken { Name = "revoked", SessionId = "sid-1", Status = Statuses.Revoked };
        var other   = new SchemataToken { Name = "other",   SessionId = "sid-2", Status = Statuses.Valid };
        var (store, _) = NewStore(r => SetupList(r, live, revoked, other));

        var found = new List<SchemataToken>();
        await foreach (var token in store.ListBySessionAsync("sid-1")) {
            found.Add(token);
        }

        Assert.Same(live, Assert.Single(found));
    }

    [Fact]
    public async Task Get_Returns_The_Row_Stored_Under_The_Slot_Key() {
        var match = new SchemataToken { Name = "nonce-1", Parent = "users/u-1", Provider = "dpop" };
        var other = new SchemataToken { Name = "nonce-2", Parent = "users/u-1", Provider = "dpop" };
        var (store, _) = NewStore(r => SetupSingle(r, match, other));

        Assert.Same(match, await store.GetAsync("users/u-1", "dpop", "nonce-1"));
    }

    [Fact]
    public async Task GetOrCreate_Returns_The_Existing_Slot_Without_Recreating() {
        var existing = new SchemataToken { Name = "nonce-1", Parent = "users/u-1", Provider = "dpop", Value = "stored" };
        var (store, repository) = NewStore(r => SetupSingle(r, existing));

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Same(existing, row);
        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreate_Mints_A_Value_And_Expires_At_Now_Plus_Ttl() {
        var (store, repository) = NewStore(time: NewClock());

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", null, TimeSpan.FromMinutes(5));

        Assert.Matches("^[0-9A-F]{64}$", row.Value);
        Assert.Equal(Now.AddMinutes(5), row.ExpireTime);
        Assert.Equal("users/u-1", row.Parent);
        Assert.Equal("dpop",      row.Provider);
        Assert.Equal("nonce-1",   row.Name);
        repository.Verify(r => r.AddAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_Returns_The_Winner_When_Slot_Creation_Hits_The_Unique_Index() {
        var winner = new SchemataToken { Name = "nonce-1", Parent = "users/u-1", Provider = "dpop", Value = "winner" };
        var (store, repository) = NewStore(r => {
            var probe = 0;
            Func<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>,
                 CancellationToken,
                 ValueTask<SchemataToken?>> replay =
                (predicate, _) => new(
                    probe++ == 0 ? null : predicate(new[] { winner }.AsQueryable()).SingleOrDefault());

            r.Setup(x => x.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns(replay);
            r.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new AlreadyExistsException());
        });

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Same(winner, row);
    }

    [Fact]
    public async Task GetOrCreate_Returns_The_Candidate_When_The_Winner_Expires_Before_The_Reread() {
        var (store, repository) = NewStore(r => {
            r.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new AlreadyExistsException());
        });

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Equal("candidate", row.Value);
    }

    [Fact]
    public async Task Set_Updates_The_Existing_Slot_Value_And_Ttl() {
        var existing = new SchemataToken { Name = "rate:k-1", Parent = null, Provider = "device", Value = "1" };
        var (store, repository) = NewStore(r => SetupSingle(r, existing), NewClock());

        await store.SetAsync(null, "device", "rate:k-1", "2", TimeSpan.FromSeconds(10));

        Assert.Equal("2",                  existing.Value);
        Assert.Equal(Now.AddSeconds(10),   existing.ExpireTime);
        repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_Creates_The_Slot_When_Absent() {
        var (store, repository) = NewStore();

        await store.SetAsync("users/u-1", "dpop", "nonce-1", "value", null);

        repository.Verify(
            r => r.AddAsync(
                It.Is<SchemataToken>(t => t.Parent == "users/u-1" && t.Provider == "dpop" && t.Name == "nonce-1"
                                       && t.Value == "value" && t.ExpireTime == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_Deletes_The_Slot_And_Commits() {
        var existing = new SchemataToken { Name = "nonce-1", Parent = "users/u-1", Provider = "dpop" };
        var (store, repository) = NewStore(r => SetupSingle(r, existing));

        await store.RemoveAsync("users/u-1", "dpop", "nonce-1");

        repository.Verify(r => r.RemoveAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_An_Absent_Slot_Writes_Nothing() {
        var (store, repository) = NewStore();

        await store.RemoveAsync("users/u-1", "dpop", "nonce-1");

        repository.Verify(r => r.RemoveAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (
        RepositoryTokenStore Store,
        Mock<IRepository<SchemataToken>>    Repository
    ) NewStore(Action<Mock<IRepository<SchemataToken>>>? configure = null, Mock<TimeProvider>? time = null) {
        var repository = new Mock<IRepository<SchemataToken>>();
        configure?.Invoke(repository);

        return (new(repository.Object, time?.Object), repository);
    }

    private static Mock<TimeProvider> NewClock() {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(Now, TimeSpan.Zero));

        return time;
    }

    private static void SetupSingle(
        Mock<IRepository<SchemataToken>> repository,
        params SchemataToken[]           rows
    ) {
        Func<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>,
             CancellationToken,
             ValueTask<SchemataToken?>> replay =
            (predicate, _) => new(predicate(rows.AsQueryable()).SingleOrDefault());

        repository.Setup(r => r.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                        It.IsAny<CancellationToken>()))
                   .Returns(replay);
    }

    private static void SetupList(Mock<IRepository<SchemataToken>> repository, params SchemataToken[] rows) {
        repository.Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>> predicate,
                            CancellationToken _) => EnumerateAsync(predicate(rows.AsQueryable())));
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
