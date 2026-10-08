using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;
using Schemata.Security.Skeleton;
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
    }

    [Fact]
    public async Task TryRedeem_Returns_False_When_The_Row_Disappeared_Before_The_Cas_Write() {
        var (store, repository) = NewStore(r => {
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        });
        var token = new SchemataToken { Name = "code", Status = Statuses.Valid, ReferenceId = "ref-code" };

        var redeemed = await store.TryRedeemAsync(token);

        Assert.False(redeemed);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryRedeem_Returns_False_When_Another_Writer_Already_Redeemed_The_Row() {
        var stamp = Guid.NewGuid();
        var (store, _) = NewStore(r => {
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
            SetupStoredRows(r, new SchemataToken {
                ReferenceId = "ref-code", Status = Statuses.Redeemed, Timestamp = Guid.NewGuid(),
            });
        });
        var token = new SchemataToken { Name = "code", Status = Statuses.Valid, ReferenceId = "ref-code", Timestamp = stamp };

        Assert.False(await store.TryRedeemAsync(token));
    }

    [Fact]
    public async Task TryRedeem_Returns_False_When_The_Row_Was_Concurrently_Mutated() {
        var stamp = Guid.NewGuid();
        var (store, _) = NewStore(r => {
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
            SetupStoredRows(r, new SchemataToken {
                ReferenceId = "ref-code", Status = Statuses.Valid, Timestamp = Guid.NewGuid(),
            });
        });
        var token = new SchemataToken { Name = "code", Status = Statuses.Valid, ReferenceId = "ref-code", Timestamp = stamp };

        Assert.False(await store.TryRedeemAsync(token));
    }

    [Fact]
    public async Task TryRedeem_Throws_When_The_Row_Is_Unchanged_But_The_Cas_Matched_Zero_Rows() {
        var stamp = Guid.NewGuid();
        var (store, _) = NewStore(r => {
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
            SetupStoredRows(r, new SchemataToken {
                ReferenceId = "ref-code", Status = Statuses.Valid, Timestamp = stamp,
            });
        });
        var token = new SchemataToken { Name = "code", Status = Statuses.Valid, ReferenceId = "ref-code", Timestamp = stamp };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.TryRedeemAsync(token));

        Assert.Contains("not a replay", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryRotate_Redeems_The_Predecessor_And_Publishes_The_Successors_Through_One_Unit_Of_Work() {
        var uow = new Mock<IUnitOfWork>();
        var (store, repository) = NewStore(r => r.Setup(value => value.Begin()).Returns(uow.Object));
        var predecessor = new SchemataToken {
            Name = "rat", ReferenceId = "rat-1", Type = "registration", Status = Statuses.Valid,
            Timestamp = Guid.NewGuid(),
        };
        var open = new SchemataToken {
            Name = "rat-open", ReferenceId = "rat-2", Type = "registration", Status = Statuses.Valid,
        };
        var bounded = new SchemataToken {
            Name = "rat-bounded", ReferenceId = "rat-3", Type = "registration", Status = Statuses.Valid,
            ExpireTime = Now.AddHours(2),
        };

        var rotated = await store.TryRotateAsync(predecessor, [open, bounded]);

        Assert.True(rotated);
        Assert.Equal(Statuses.Redeemed, predecessor.Status);
        // Successor rows pass through untouched; expiry is the row factory's authority.
        Assert.Null(open.ExpireTime);
        Assert.Equal(Now.AddHours(2), bounded.ExpireTime);
        repository.Verify(value => value.UpdateAsync(predecessor, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.AddRangeAsync(
            It.Is<IEnumerable<SchemataToken>>(rows => rows.SequenceEqual(new[] { open, bounded })),
            It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryRotate_Requires_A_Valid_Predecessor_And_Nonempty_Successors() {
        var (store, repository) = NewStore();
        var spent = new SchemataToken { Name = "rat", ReferenceId = "rat-1", Status = Statuses.Redeemed };
        var valid = new SchemataToken { Name = "rat", ReferenceId = "rat-1", Status = Statuses.Valid };
        var successor = new SchemataToken { Name = "rat-next", ReferenceId = "rat-2", Status = Statuses.Valid };

        Assert.False(await store.TryRotateAsync(spent, [successor]));
        Assert.False(await store.TryRotateAsync(valid, []));

        Assert.Equal(Statuses.Redeemed, spent.Status);
        Assert.Equal(Statuses.Valid, valid.Status);
        repository.Verify(value => value.Begin(), Times.Never);
    }

    [Fact]
    public async Task TryRotate_Returns_False_When_The_Predecessor_Moved_Before_The_Commit() {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        var (store, _) = NewStore(r => {
            r.Setup(value => value.Begin()).Returns(uow.Object);
            SetupStoredRows(r, new SchemataToken {
                ReferenceId = "rat-1", Status = Statuses.Redeemed, Timestamp = Guid.NewGuid(),
            });
        });
        var predecessor = new SchemataToken {
            Name = "rat", ReferenceId = "rat-1", Status = Statuses.Valid, Timestamp = Guid.NewGuid(),
        };
        var successor = new SchemataToken { Name = "rat-next", ReferenceId = "rat-2", Status = Statuses.Valid };

        Assert.False(await store.TryRotateAsync(predecessor, [successor]));
    }

    [Fact]
    public async Task TryRotate_Throws_When_The_Predecessor_Is_Unchanged_After_The_Abort() {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        var stamp = Guid.NewGuid();
        var (store, _) = NewStore(r => {
            r.Setup(value => value.Begin()).Returns(uow.Object);
            SetupStoredRows(r, new SchemataToken {
                ReferenceId = "rat-1", Status = Statuses.Valid, Timestamp = stamp,
            });
        });
        var predecessor = new SchemataToken {
            Name = "rat", ReferenceId = "rat-1", Status = Statuses.Valid, Timestamp = stamp,
        };
        var successor = new SchemataToken { Name = "rat-next", ReferenceId = "rat-2", Status = Statuses.Valid };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.TryRotateAsync(predecessor, [successor]));

        Assert.Contains("not a lost race", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryRotate_Classifies_A_Unique_Collision_Through_A_Fresh_Store() {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
           .ThrowsAsync(new AlreadyExistsException());
        var attempt = new Mock<IRepository<SchemataToken>>();
        attempt.Setup(value => value.Begin()).Returns(uow.Object);
        var reread = new Mock<IRepository<SchemataToken>>();
        SetupStoredRows(reread, new SchemataToken {
            ReferenceId = "rat-1", Status = Statuses.Redeemed, Timestamp = Guid.NewGuid(),
        });
        var scopes = NewScopeFactory(TimeProvider.System, reread);
        var store  = new RepositoryTokenStore(NewProvider(attempt.Object), TimeProvider.System, scopes.Object);
        var predecessor = new SchemataToken {
            Name = "rat", ReferenceId = "rat-1", Status = Statuses.Valid, Timestamp = Guid.NewGuid(),
        };
        var successor = new SchemataToken { Name = "rat-next", ReferenceId = "rat-2", Status = Statuses.Valid };

        Assert.False(await store.TryRotateAsync(predecessor, [successor]));

        reread.Verify(value => value.AnyAsync(
            It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        attempt.Verify(value => value.AnyAsync(
            It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        scopes.Verify(factory => factory.CreateScope(), Times.Once);
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
    public async Task Revoke_By_Authorization_Drains_The_Stream_Before_The_First_Update() {
        var spy     = new StreamSpy();
        var live1   = new SchemataToken { Name = "live-1",  Authorization = "auth-1", Status = Statuses.Valid };
        var live2   = new SchemataToken { Name = "live-2",  Authorization = "auth-1", Status = Statuses.Redeemed };
        var already = new SchemataToken { Name = "already", Authorization = "auth-1", Status = Statuses.Revoked };
        var away    = new SchemataToken { Name = "away",    Authorization = "auth-2", Status = Statuses.Valid };
        var (store, repository) = NewStore(r => {
            SetupList(r, spy, live1, live2, already, away);
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .Callback(spy.ObserveMutation);
        });

        var count = await store.RevokeByAuthorizationAsync("auth-1");

        Assert.False(spy.MutatedWhileOpen);
        Assert.Equal(2, count);
        Assert.Equal(Statuses.Revoked, live1.Status);
        Assert.Equal(Statuses.Revoked, live2.Status);
        Assert.Equal(Statuses.Valid,   away.Status);
        repository.Verify(r => r.UpdateAsync(already, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_By_Session_Drains_The_Stream_Before_The_First_Update() {
        var spy   = new StreamSpy();
        var live1 = new SchemataToken { Name = "live-1", SessionId = "sid-1", Status = Statuses.Valid };
        var live2 = new SchemataToken { Name = "live-2", SessionId = "sid-1", Status = Statuses.Redeemed };
        var away  = new SchemataToken { Name = "away",   SessionId = "sid-2", Status = Statuses.Valid };
        var (store, repository) = NewStore(r => {
            SetupList(r, spy, live1, live2, away);
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .Callback(spy.ObserveMutation);
        });

        var count = await store.RevokeBySessionAsync("sid-1");

        Assert.False(spy.MutatedWhileOpen);
        Assert.Equal(2, count);
        Assert.Equal(Statuses.Revoked, live1.Status);
        Assert.Equal(Statuses.Revoked, live2.Status);
        Assert.Equal(Statuses.Valid,   away.Status);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_By_Device_Drains_The_Stream_Before_The_First_Update() {
        var spy   = new StreamSpy();
        var live1 = new SchemataToken { Name = "live-1", DeviceId = "device-1", Status = Statuses.Valid };
        var live2 = new SchemataToken { Name = "live-2", DeviceId = "device-1", Status = Statuses.Redeemed };
        var away  = new SchemataToken { Name = "away",   DeviceId = "device-2", Status = Statuses.Valid };
        var (store, repository) = NewStore(r => {
            SetupList(r, spy, live1, live2, away);
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .Callback(spy.ObserveMutation);
        });

        var count = await store.RevokeByDeviceAsync("device-1");

        Assert.False(spy.MutatedWhileOpen);
        Assert.Equal(2, count);
        Assert.Equal(Statuses.Revoked, live1.Status);
        Assert.Equal(Statuses.Revoked, live2.Status);
        Assert.Equal(Statuses.Valid,   away.Status);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Prune_Drains_The_Stream_Before_The_First_Removal() {
        var spy     = new StreamSpy();
        var expired = new SchemataToken { Name = "expired", Status = Statuses.Valid, ExpireTime = Now.AddSeconds(-1) };
        var revoked = new SchemataToken { Name = "revoked", Status = Statuses.Revoked };
        var live    = new SchemataToken { Name = "live",    Status = Statuses.Valid, ExpireTime = Now.AddMinutes(5) };
        var (store, repository) = NewStore(r => {
            SetupList(r, spy, expired, revoked, live);
            r.Setup(x => x.RemoveAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .Callback(spy.ObserveMutation);
        }, NewClock());

        var count = await store.PruneAsync();

        Assert.False(spy.MutatedWhileOpen);
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
        var match = new SchemataToken { Name = "assigned-1", Key = "nonce-1", Parent = "users/u-1", Provider = "dpop" };
        var other = new SchemataToken { Name = "nonce-1", Key = "nonce-2", Parent = "users/u-1", Provider = "dpop" };
        var (store, _) = NewStore(r => SetupSingle(r, match, other));

        Assert.Same(match, await store.GetAsync("users/u-1", "dpop", "nonce-1"));
    }

    [Fact]
    public async Task GetOrCreate_Returns_The_Existing_Slot_Without_Recreating() {
        var existing = new SchemataToken { Name = "assigned-1", Key = "nonce-1", Parent = "users/u-1", Provider = "dpop", Value = "stored" };
        var (store, repository) = NewStore(r => SetupSingle(r, existing));

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Same(existing, row);
        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreate_Mints_A_Value_And_Expires_At_Now_Plus_Ttl() {
        var transaction = new Mock<IUnitOfWork>();
        var (store, repository) = NewStore(r => r.Setup(value => value.Begin()).Returns(transaction.Object), NewClock());

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", null, TimeSpan.FromMinutes(5));

        Assert.Matches("^[0-9A-F]{64}$", row.Value);
        Assert.Equal(Now.AddMinutes(5), row.ExpireTime);
        Assert.Equal("users/u-1", row.Parent);
        Assert.Equal("dpop",      row.Provider);
        Assert.Equal("nonce-1",   row.Key);
        Assert.Null(row.Name);
        repository.Verify(r => r.AddAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreate_Returns_The_Winner_When_Slot_Creation_Hits_The_Unique_Index() {
        var winner = new SchemataToken { Name = "assigned-winner", Key = "nonce-1", Parent = "users/u-1", Provider = "dpop", Value = "winner" };
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
            // The slot insert commits its own transaction; the unique index surfaces there.
            var insertion = new Mock<IUnitOfWork>();
            insertion.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new AlreadyExistsException());
            r.Setup(x => x.Begin()).Returns(insertion.Object);
        });

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Same(winner, row);
    }

    [Fact]
    public async Task GetOrCreate_Fails_Closed_When_The_Winner_Expires_Before_The_Reread() {
        var winner = new SchemataToken {
            Name = "assigned-winner", Key = "nonce-1", Parent = "users/u-1", Provider = "dpop",
            Value = "winner", ExpireTime = Now.AddMinutes(-5),
        };
        var (store, _) = NewStore(r => {
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
            // The slot insert commits its own transaction; the unique index surfaces there.
            var insertion = new Mock<IUnitOfWork>();
            insertion.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new AlreadyExistsException());
            r.Setup(x => x.Begin()).Returns(insertion.Object);
        }, NewClock());

        await Assert.ThrowsAsync<AbortedException>(
            () => store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task GetOrCreate_Fails_Closed_When_The_Winner_Disappears_After_A_Lost_Cas() {
        var existing = new SchemataToken {
            Name = "assigned-old", Key = "sid-1", Parent = "users/u-1", Provider = "op-session",
            Value = "gen-old", ExpireTime = Now.AddMinutes(-5),
        };
        var (store, _) = NewStore(r => {
            var probe = 0;
            Func<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>,
                 CancellationToken,
                 ValueTask<SchemataToken?>> replay =
                (predicate, _) => new(
                    probe++ == 0 ? predicate(new[] { existing }.AsQueryable()).SingleOrDefault() : null);

            r.Setup(x => x.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns(replay);
            r.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        }, NewClock());

        await Assert.ThrowsAsync<AbortedException>(
            () => store.GetOrCreateAsync("users/u-1", "op-session", "sid-1", "gen-loser", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task GetOrCreate_Establishes_Through_A_Fresh_Scope_Without_Touching_The_Caller_Repository() {
        var time   = NewClock();
        var scoped = new Mock<IRepository<SchemataToken>>();
        var transaction = new Mock<IUnitOfWork>();
        var scopes = NewScopeFactory(time.Object, scoped);
        scoped.Setup(x => x.Begin()).Returns(transaction.Object);
        var caller = new Mock<IRepository<SchemataToken>>();
        var store  = new RepositoryTokenStore(NewProvider(caller.Object), time.Object, scopes.Object);

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Equal("candidate", row.Value);
        Assert.Equal(Now.AddMinutes(5), row.ExpireTime);
        scoped.Verify(r => r.AddAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        scoped.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        scopes.Verify(f => f.CreateScope(), Times.Once);
        caller.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrCreate_Returns_The_Winner_From_A_Fresh_Scope_When_The_First_Attempt_Loses_The_Cas() {
        var time = NewClock();
        var expired = new SchemataToken {
            Name = "assigned-old", Key = "sid-1", Parent = "users/u-1", Provider = "op-session",
            Value = "gen-old", ExpireTime = Now.AddMinutes(-5),
        };
        var winner = new SchemataToken {
            Name = "assigned-winner", Key = "sid-1", Parent = "users/u-1", Provider = "op-session",
            Value = "gen-winner", ExpireTime = Now.AddMinutes(5),
        };
        var attempt = new Mock<IRepository<SchemataToken>>();
        SetupSingle(attempt, expired);
        attempt.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        var reread = new Mock<IRepository<SchemataToken>>();
        SetupSingle(reread, winner);
        var scopes = NewScopeFactory(time.Object, attempt, reread);
        var caller = new Mock<IRepository<SchemataToken>>();
        var store  = new RepositoryTokenStore(NewProvider(caller.Object), time.Object, scopes.Object);

        var row = await store.GetOrCreateAsync("users/u-1", "op-session", "sid-1", "gen-loser", TimeSpan.FromMinutes(5));

        Assert.Same(winner, row);
        scopes.Verify(f => f.CreateScope(), Times.Exactly(2));
        caller.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrCreate_Propagates_A_Second_Contention_After_One_Fresh_Scope_Retry() {
        var time = NewClock();
        var first = new Mock<IRepository<SchemataToken>>();
        // Each establish attempt commits its own transaction; the unique index surfaces there.
        var firstInsert = new Mock<IUnitOfWork>();
        firstInsert.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new AlreadyExistsException());
        var firstReread = new Mock<IRepository<SchemataToken>>();
        var second = new Mock<IRepository<SchemataToken>>();
        var secondInsert = new Mock<IUnitOfWork>();
        secondInsert.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new AlreadyExistsException());
        var secondReread = new Mock<IRepository<SchemataToken>>();
        var scopes = NewScopeFactory(time.Object, first, firstReread, second, secondReread);
        first.Setup(x => x.Begin()).Returns(firstInsert.Object);
        second.Setup(x => x.Begin()).Returns(secondInsert.Object);
        var caller = new Mock<IRepository<SchemataToken>>();
        var store  = new RepositoryTokenStore(NewProvider(caller.Object), time.Object, scopes.Object);

        await Assert.ThrowsAsync<AbortedException>(
            () => store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5)));

        scopes.Verify(f => f.CreateScope(), Times.Exactly(4));
        caller.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrCreate_Replaces_An_Expired_Slot_In_Place() {
        var existing = new SchemataToken {
            Name = "assigned-1", Key = "nonce-1", Parent = "users/u-1", Provider = "dpop",
            Value = "stale", ExpireTime = Now.AddMinutes(-5),
        };
        var (store, repository) = NewStore(r => SetupSingle(r, existing), NewClock());

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Same(existing, row);
        Assert.Equal("candidate", row.Value);
        Assert.Equal(Now.AddMinutes(5), row.ExpireTime);
        repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreate_Reuses_A_Live_Slot_Without_Extending_Its_Expiry() {
        var existing = new SchemataToken {
            Name = "assigned-1", Key = "nonce-1", Parent = "users/u-1", Provider = "dpop",
            Value = "live", ExpireTime = Now.AddHours(1),
        };
        var (store, repository) = NewStore(r => SetupSingle(r, existing), NewClock());

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "nonce-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Same(existing, row);
        Assert.Equal("live", row.Value);
        Assert.Equal(Now.AddHours(1), row.ExpireTime);
        repository.Verify(r => r.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreate_ReReads_The_Winner_When_A_Concurrent_Replace_Wins_The_Cas_Race() {
        var stored = new SchemataToken {
            Name = "assigned-old", Key = "sid-1", Parent = "users/u-1", Provider = "op-session",
            Value = "gen-old", ExpireTime = Now.AddMinutes(-5), Timestamp = Guid.NewGuid(),
        };
        var (store, repository) = NewStore(time: NewClock());
        repository.Setup(x => x.SingleOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>> predicate, CancellationToken _) => {
                      // Reads hand out load-time snapshots, mirroring a tracked repository read.
                      var match = predicate(new[] { stored }.AsQueryable()).SingleOrDefault();
                      return new(match is null ? null : new SchemataToken {
                          Name = match.Name, Parent = match.Parent, Provider = match.Provider, Key = match.Key,
                          Value = match.Value, ExpireTime = match.ExpireTime, Timestamp = match.Timestamp,
                      });
                  });
        repository.Setup(x => x.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
                  .Returns(() => {
                      // A concurrent replacer committed first, so the stale Timestamp CAS matches zero rows.
                      stored.Value      = "gen-winner";
                      stored.ExpireTime = Now.AddMinutes(5);
                      stored.Timestamp  = Guid.NewGuid();
                      throw SchemataResourceErrors.Aborted<SchemataToken>();
                  });

        var row = await store.GetOrCreateAsync("users/u-1", "op-session", "sid-1", "gen-loser", TimeSpan.FromMinutes(5));

        Assert.Equal("gen-winner", row.Value);
        Assert.Equal(Now.AddMinutes(5), row.ExpireTime);
    }

    [Fact]
    public async Task Set_Updates_The_Existing_Slot_Value_And_Ttl() {
        var existing = new SchemataToken { Name = "assigned-rate", Key = "rate:k-1", Parent = null, Provider = "device", Value = "1" };
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
                It.Is<SchemataToken>(t => t.Parent == "users/u-1" && t.Provider == "dpop" && t.Key == "nonce-1" && t.Name == null
                                       && t.Value == "value" && t.ExpireTime == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_Deletes_The_Slot_And_Commits() {
        var existing = new SchemataToken { Name = "assigned-1", Key = "nonce-1", Parent = "users/u-1", Provider = "dpop" };
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublishFamily_PropagatesUnclassifiedOrNonTokenConflicts_WithoutInvalidatingFamily(bool classified) {
        var conflict = classified
            ? SchemataResourceErrors.Aborted<SchemataSecurity>("securities/key")
            : new AbortedException();
        var transaction = new Mock<IUnitOfWork>();
        transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(conflict);
        var marker = new SchemataToken { Family = "shared", FamilyKey = "shared", Type = TokenTypes.Family, Status = Statuses.FamilyActive };
        var (store, repository) = NewStore(r => {
            r.Setup(t => t.Begin()).Returns(transaction.Object);
            SetupSingle(r, marker);
        });

        var error = await Assert.ThrowsAsync<AbortedException>(() => store.PublishFamilyAsync("shared",
            [new() { Family = "shared", Type = "access_token", Status = Statuses.Valid }], create: false));

        Assert.Same(conflict, error);
        Assert.Equal(Statuses.FamilyActive, marker.Status);
        repository.Verify(r => r.UpdateAsync(It.Is<SchemataToken>(t => t.Status == Statuses.FamilyInvalidated),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_Family_Commits_Marker_And_Successors_Through_One_Unit_Of_Work() {
        var uow = new Mock<IUnitOfWork>();
        var (store, repository) = NewStore(r => r.Setup(value => value.Begin()).Returns(uow.Object));
        var access = new SchemataToken { Family = "family-1", Type = "access_token", Status = Statuses.Valid };
        var refresh = new SchemataToken { Family = "family-1", Type = "refresh_token", Status = Statuses.Valid };

        var published = await store.PublishFamilyAsync("family-1", [access, refresh], create: true);

        Assert.True(published);
        repository.Verify(value => value.AddAsync(
            It.Is<SchemataToken>(token => token.Type == TokenTypes.Family
                                       && token.Status == Statuses.FamilyActive
                                       && token.Family == "family-1"
                                       && token.FamilyKey == "family-1"), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.AddRangeAsync(
            It.Is<IEnumerable<SchemataToken>>(rows => rows.SequenceEqual(new[] { access, refresh })),
            It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rotate_Family_Transitions_Predecessor_And_Publishes_Successors_Atomically() {
        var uow = new Mock<IUnitOfWork>();
        var marker = new SchemataToken { Family = "family-1", FamilyKey = "family-1", Type = TokenTypes.Family, Status = Statuses.FamilyActive };
        var predecessor = new SchemataToken {
            Family = "family-1", ReferenceId = "rt-1", Type = "refresh_token", Status = Statuses.Valid,
        };
        var successor = new SchemataToken { Family = "family-1", Type = "refresh_token", Status = Statuses.Valid };
        var (store, repository) = NewStore(r => {
            r.Setup(value => value.Begin()).Returns(uow.Object);
            SetupSingle(r, marker);
        });

        var rotated = await store.RotateFamilyAsync(predecessor, [successor]);

        Assert.True(rotated);
        Assert.Equal(Statuses.Redeemed, predecessor.Status);
        repository.Verify(value => value.UpdateAsync(predecessor, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(value => value.AddRangeAsync(
            It.Is<IEnumerable<SchemataToken>>(rows => rows.Single() == successor), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalidate_Family_Revokes_Only_Family_Members_And_Tombstones_The_Marker() {
        var uow = new Mock<IUnitOfWork>();
        var marker = new SchemataToken { Family = "family-1", FamilyKey = "family-1", Type = TokenTypes.Family, Status = Statuses.FamilyActive };
        var access = new SchemataToken { Family = "family-1", Type = "access_token", Status = Statuses.Valid };
        var unrelated = new SchemataToken { Family = "family-2", Type = "access_token", Status = Statuses.Valid };
        var (store, repository) = NewStore(r => {
            r.Setup(value => value.Begin()).Returns(uow.Object);
            SetupSingle(r, marker);
            SetupList(r, access, unrelated);
        });

        Assert.True(await store.InvalidateFamilyAsync("family-1"));

        Assert.Equal(Statuses.FamilyInvalidated, marker.Status);
        Assert.Equal(Statuses.Revoked, access.Status);
        Assert.Equal(Statuses.Valid, unrelated.Status);
        uow.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_Participant_Persists_One_Fact_Row_Without_Credential_Expiry() {
        var transaction = new Mock<IUnitOfWork>();
        var (store, repository) = NewStore(r => r.Setup(value => value.Begin()).Returns(transaction.Object));

        await store.RegisterParticipantAsync("users/u-1", "sid-1", "applications/client-1");

        repository.Verify(r => r.AddAsync(
            It.Is<SchemataToken>(t => t.Type == TokenTypes.SessionParticipant
                                   && t.Status == Statuses.Valid
                                   && t.Parent == "users/u-1"
                                   && t.Provider == TokenTypes.SessionParticipant
                                   && t.Key == "sid-1\u001eapplications/client-1"
                                   && t.SessionId == "sid-1"
                                   && t.Application == "applications/client-1"
                                   && t.ExpireTime == null
                                   && t.Name == null),
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_Participant_Is_Idempotent_Per_Subject_Session_And_Application() {
        var existing = Participant("users/u-1", "sid-1", "applications/client-1");
        var (store, repository) = NewStore(r => SetupSingle(r, existing));

        await store.RegisterParticipantAsync("users/u-1", "sid-1", "applications/client-1");

        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_Participant_Revalidates_A_Row_Revoked_Out_Of_Band() {
        var existing = Participant("users/u-1", "sid-1", "applications/client-1");
        existing.Status     = Statuses.Revoked;
        existing.ExpireTime = Now.AddMinutes(-1);
        var transaction = new Mock<IUnitOfWork>();
        var (store, repository) = NewStore(r => {
            r.Setup(value => value.Begin()).Returns(transaction.Object);
            SetupSingle(r, existing);
        });

        await store.RegisterParticipantAsync("users/u-1", "sid-1", "applications/client-1");

        Assert.Equal(Statuses.Valid, existing.Status);
        Assert.Null(existing.ExpireTime);
        repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_Participant_Treats_A_Concurrent_Create_Race_As_Success() {
        // The attempt loses the unique-index race; the winner re-read through a fresh scope
        // confirms the exact participation fact, so the registration succeeds.
        var winner  = Participant("users/u-1", "sid-1", "applications/client-1");
        var attempt = new Mock<IRepository<SchemataToken>>();
        attempt.Setup(x => x.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new AlreadyExistsException());
        var reread = new Mock<IRepository<SchemataToken>>();
        SetupSingle(reread, winner);
        var scopes = NewScopeFactory(TimeProvider.System, attempt, reread);
        var caller = new Mock<IRepository<SchemataToken>>();
        var store  = new RepositoryTokenStore(NewProvider(caller.Object), TimeProvider.System, scopes.Object);

        await store.RegisterParticipantAsync("users/u-1", "sid-1", "applications/client-1");

        attempt.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        caller.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_Participant_Propagates_An_Unrelated_Collision() {
        // A naming-advisor Name collision surfaces through the same exception type but leaves no
        // participation fact behind; the original failure must propagate instead of reporting a
        // registration that never happened.
        var attempt = new Mock<IRepository<SchemataToken>>();
        attempt.Setup(x => x.AddAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new AlreadyExistsException());
        var reread = new Mock<IRepository<SchemataToken>>();
        var scopes = NewScopeFactory(TimeProvider.System, attempt, reread);
        var caller = new Mock<IRepository<SchemataToken>>();
        var store  = new RepositoryTokenStore(NewProvider(caller.Object), TimeProvider.System, scopes.Object);

        await Assert.ThrowsAsync<AlreadyExistsException>(
            () => store.RegisterParticipantAsync("users/u-1", "sid-1", "applications/client-1"));

        caller.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Register_Participant_Ignores_An_Incomplete_Fact() {
        var (store, repository) = NewStore();

        await store.RegisterParticipantAsync(null, "sid-1", "applications/client-1");
        await store.RegisterParticipantAsync("users/u-1", "  ", "applications/client-1");
        await store.RegisterParticipantAsync("users/u-1", "sid-1", null);

        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_Participants_For_Subject_And_Session_Excludes_Other_Subjects_On_The_Same_Session() {
        var first     = Participant("users/u-1", "sid-1", "applications/client-1");
        var other     = Participant("users/u-2", "sid-1", "applications/client-2");
        var duplicate = Participant("users/u-1", "sid-1", "applications/client-1");
        var elsewhere = Participant("users/u-1", "sid-2", "applications/client-3");
        var retired   = Participant("users/u-1", "sid-1", "applications/client-4");
        retired.Status = Statuses.Revoked;
        var credential = new SchemataToken {
            Type = "access_token", Status = Statuses.Valid, SessionId = "sid-1",
            Application = "applications/client-9",
        };
        var (store, _) = NewStore(r => SetupList(r, first, other, duplicate, elsewhere, retired, credential));

        var applications = await store.ListParticipantsAsync("users/u-1", "sid-1");

        Assert.Equal(["applications/client-1"], applications);
    }

    [Fact]
    public async Task List_Participants_Without_A_Session_Spans_The_Subjects_Sessions() {
        var first = Participant("users/u-1", "sid-1", "applications/client-1");
        var other = Participant("users/u-1", "sid-2", "applications/client-2");
        var alien = Participant("users/u-2", "sid-1", "applications/client-3");
        var (store, _) = NewStore(r => SetupList(r, first, other, alien));

        var applications = await store.ListParticipantsAsync("users/u-1", null);

        Assert.Equal(["applications/client-1", "applications/client-2"], applications);
    }

    [Fact]
    public async Task List_Participants_Without_Subject_Or_Session_Reads_Nothing() {
        var (store, repository) = NewStore();

        Assert.Empty(await store.ListParticipantsAsync(null, null));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Retire_Participants_Removes_Only_The_Targeted_Session() {
        var first  = Participant("users/u-1", "sid-1", "applications/client-1");
        var second = Participant("users/u-1", "sid-1", "applications/client-2");
        var spared = Participant("users/u-1", "sid-2", "applications/client-3");
        var (store, repository) = NewStore(r => SetupList(r, first, second, spared));

        var count = await store.RetireParticipantsAsync("users/u-1", "sid-1");

        Assert.Equal(2, count);
        repository.Verify(r => r.RemoveAsync(first, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RemoveAsync(second, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RemoveAsync(spared, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Retire_Participants_By_Subject_Leaves_Other_Subjects_Intact() {
        var mine  = Participant("users/u-1", "sid-1", "applications/client-1");
        var alien = Participant("users/u-2", "sid-1", "applications/client-2");
        var (store, repository) = NewStore(r => SetupList(r, mine, alien));

        var count = await store.RetireParticipantsAsync("users/u-2", null);

        Assert.Equal(1, count);
        repository.Verify(r => r.RemoveAsync(alien, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RemoveAsync(mine, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Retire_Participants_For_Subject_And_Session_Leaves_Other_Subjects_On_The_Same_Session() {
        var mine  = Participant("users/u-1", "sid-1", "applications/client-1");
        var alien = Participant("users/u-2", "sid-1", "applications/client-2");
        var (store, repository) = NewStore(r => SetupList(r, mine, alien));

        var count = await store.RetireParticipantsAsync("users/u-1", "sid-1");

        Assert.Equal(1, count);
        repository.Verify(r => r.RemoveAsync(mine, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.RemoveAsync(alien, It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_Participant_Writes_Through_A_Fresh_Scope_Without_Touching_The_Caller_Repository() {
        var scoped = new Mock<IRepository<SchemataToken>>();
        var transaction = new Mock<IUnitOfWork>();
        var scopes = NewScopeFactory(TimeProvider.System, scoped);
        scoped.Setup(x => x.Begin()).Returns(transaction.Object);
        var caller = new Mock<IRepository<SchemataToken>>();
        var store  = new RepositoryTokenStore(NewProvider(caller.Object), TimeProvider.System, scopes.Object);

        await store.RegisterParticipantAsync("users/u-1", "sid-1", "applications/client-1");

        scoped.Verify(r => r.AddAsync(
            It.Is<SchemataToken>(t => t.Type == TokenTypes.SessionParticipant
                                   && t.Parent == "users/u-1"
                                   && t.SessionId == "sid-1"
                                   && t.Application == "applications/client-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        scoped.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        scopes.Verify(f => f.CreateScope(), Times.Once);
        caller.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Retire_Participants_Writes_Through_A_Fresh_Scope_Without_Touching_The_Caller_Repository() {
        var row    = Participant("users/u-1", "sid-1", "applications/client-1");
        var scoped = new Mock<IRepository<SchemataToken>>();
        SetupList(scoped, row);
        var scopes = NewScopeFactory(TimeProvider.System, scoped);
        var caller = new Mock<IRepository<SchemataToken>>();
        var store  = new RepositoryTokenStore(NewProvider(caller.Object), TimeProvider.System, scopes.Object);

        var count = await store.RetireParticipantsAsync("users/u-1", "sid-1");

        Assert.Equal(1, count);
        scoped.Verify(r => r.RemoveAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        scoped.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        scopes.Verify(f => f.CreateScope(), Times.Once);
        caller.VerifyNoOtherCalls();
    }


    [Fact]
    public async Task Participant_Survives_Credential_Prune() {
        var participant = Participant("users/u-1", "sid-1", "applications/client-1");
        var expired = new SchemataToken {
            Name = "expired", Type = "access_token", SessionId = "sid-1",
            Application = "applications/client-1", Status = Statuses.Valid, ExpireTime = Now.AddSeconds(-1),
        };
        var (store, repository) = NewStore(r => SetupList(r, participant, expired), NewClock());

        var pruned = await store.PruneAsync();

        Assert.Equal(1, pruned);
        Assert.Equal(Statuses.Valid, participant.Status);
        repository.Verify(r => r.RemoveAsync(participant, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Participant_Survives_Session_Credential_Revocation() {
        var participant = Participant("users/u-1", "sid-1", "applications/client-1");
        var credential = new SchemataToken {
            Name = "by-session", Type = "access_token", SessionId = "sid-1",
            Application = "applications/client-2", Status = Statuses.Valid, ExpireTime = Now.AddMinutes(5),
        };
        var (store, repository) = NewStore(r => SetupList(r, participant, credential));

        var count = await store.RevokeBySessionAsync("sid-1");

        Assert.Equal(1, count);
        Assert.Equal(Statuses.Valid, participant.Status);
        repository.Verify(r => r.UpdateAsync(participant, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Participant_Survives_Application_Credential_Revocation() {
        var participant = Participant("users/u-1", "sid-1", "applications/client-1");
        var credential = new SchemataToken {
            Name = "by-application", Type = "access_token", SessionId = "sid-2",
            Application = "applications/client-1", Status = Statuses.Valid, ExpireTime = Now.AddMinutes(5),
        };
        var (store, repository) = NewStore(r => SetupList(r, participant, credential));

        var count = await store.RevokeByApplicationAsync("applications/client-1");

        Assert.Equal(1, count);
        Assert.Equal(Statuses.Valid, participant.Status);
        repository.Verify(r => r.UpdateAsync(participant, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoke_By_Application_Retries_Through_A_Fresh_Scope_And_Drains_The_Racing_Rotation() {
        var accessUid       = Guid.NewGuid();
        var predecessorUid  = Guid.NewGuid();
        var accessStamp     = Guid.NewGuid();
        var predecessorStamp = Guid.NewGuid();
        var access = new SchemataToken {
            Uid = accessUid, Name = "access", ReferenceId = "at-1", Application = "applications/client-1",
            Type = "access_token", Status = Statuses.Valid, Timestamp = accessStamp,
        };
        var predecessor = new SchemataToken {
            Uid = predecessorUid, Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Valid, Timestamp = predecessorStamp,
        };
        var attempt = new Mock<IRepository<SchemataToken>>();
        SetupList(attempt, access, predecessor);
        var transaction = new Mock<IUnitOfWork>();
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        attempt.Setup(value => value.Begin()).Returns(transaction.Object);
        // The racing rotation redeemed the loaded predecessor (new Timestamp) and published a
        // successor; the access row is untouched.
        var storedAccess = new SchemataToken {
            Uid = accessUid, Name = "access", ReferenceId = "at-1", Application = "applications/client-1",
            Type = "access_token", Status = Statuses.Valid, Timestamp = accessStamp,
        };
        var moved = new SchemataToken {
            Uid = predecessorUid, Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Redeemed, Timestamp = Guid.NewGuid(),
        };
        var winner = new SchemataToken {
            Uid = Guid.NewGuid(), Name = "rat-next", ReferenceId = "rat-2", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Valid, Timestamp = Guid.NewGuid(),
        };
        var reread = new Mock<IRepository<SchemataToken>>();
        SetupList(reread, storedAccess, moved, winner);
        var scopes = NewScopeFactory(TimeProvider.System, attempt, reread);
        var store  = new RepositoryTokenStore(Mock.Of<IServiceProvider>(), TimeProvider.System, scopes.Object);

        var count = await store.RevokeByApplicationAsync("applications/client-1");

        Assert.Equal(3, count);
        Assert.Equal(Statuses.Revoked, storedAccess.Status);
        Assert.Equal(Statuses.Revoked, moved.Status);
        Assert.Equal(Statuses.Revoked, winner.Status);
        reread.Verify(value => value.UpdateAsync(storedAccess, It.IsAny<CancellationToken>()), Times.Once);
        reread.Verify(value => value.UpdateAsync(moved, It.IsAny<CancellationToken>()), Times.Once);
        reread.Verify(value => value.UpdateAsync(winner, It.IsAny<CancellationToken>()), Times.Once);
        reread.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        scopes.Verify(factory => factory.CreateScope(), Times.Exactly(2));
    }

    [Fact]
    public async Task Revoke_By_Application_Classifies_An_Immediate_Update_Failure_And_Retries() {
        var uid   = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var predecessor = new SchemataToken {
            Uid = uid, Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Valid, Timestamp = stamp,
        };
        var attempt = new Mock<IRepository<SchemataToken>>();
        SetupList(attempt, predecessor);
        attempt.Setup(value => value.Begin()).Returns(new Mock<IUnitOfWork>().Object);
        // LinqToDB surfaces an optimistic failure at the update itself, before the commit.
        attempt.Setup(value => value.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        var moved = new SchemataToken {
            Uid = uid, Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Redeemed, Timestamp = Guid.NewGuid(),
        };
        var winner = new SchemataToken {
            Uid = Guid.NewGuid(), Name = "rat-next", ReferenceId = "rat-2", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Valid, Timestamp = Guid.NewGuid(),
        };
        var reread = new Mock<IRepository<SchemataToken>>();
        SetupList(reread, moved, winner);
        var scopes = NewScopeFactory(TimeProvider.System, attempt, reread);
        var store  = new RepositoryTokenStore(Mock.Of<IServiceProvider>(), TimeProvider.System, scopes.Object);

        var count = await store.RevokeByApplicationAsync("applications/client-1");

        Assert.Equal(2, count);
        Assert.Equal(Statuses.Revoked, moved.Status);
        Assert.Equal(Statuses.Revoked, winner.Status);
        reread.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        scopes.Verify(factory => factory.CreateScope(), Times.Exactly(2));
    }

    [Fact]
    public async Task Revoke_By_Application_Throws_When_The_Abort_Left_The_Whole_Batch_Unchanged() {
        var uid   = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var predecessor = new SchemataToken {
            Uid = uid, Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Valid, Timestamp = stamp,
        };
        var attempt = new Mock<IRepository<SchemataToken>>();
        SetupList(attempt, predecessor);
        var transaction = new Mock<IUnitOfWork>();
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());
        attempt.Setup(value => value.Begin()).Returns(transaction.Object);
        // The CAS matched zero rows while the stored row still carries exactly the loaded stamp.
        var stored = new SchemataToken {
            Uid = uid, Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Valid, Timestamp = stamp,
        };
        var reread = new Mock<IRepository<SchemataToken>>();
        SetupList(reread, stored);
        var scopes = NewScopeFactory(TimeProvider.System, attempt, reread);
        var store  = new RepositoryTokenStore(Mock.Of<IServiceProvider>(), TimeProvider.System, scopes.Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RevokeByApplicationAsync("applications/client-1"));

        Assert.Contains("not a lost race", error.Message, StringComparison.Ordinal);
        reread.Verify(value => value.Begin(), Times.Never);
        reread.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        scopes.Verify(factory => factory.CreateScope(), Times.Exactly(2));
    }

    [Fact]
    public async Task Revoke_By_Application_Is_Idempotent_Within_One_Store() {
        var access = new SchemataToken {
            Uid = Guid.NewGuid(), Name = "access", ReferenceId = "at-1", Application = "applications/client-1",
            Type = "access_token", Status = Statuses.Valid, Timestamp = Guid.NewGuid(),
        };
        var registration = new SchemataToken {
            Uid = Guid.NewGuid(), Name = "rat", ReferenceId = "rat-1", Application = "applications/client-1",
            Type = "registration", Status = Statuses.Valid, Timestamp = Guid.NewGuid(),
        };
        var (store, repository) = NewStore(r => SetupList(r, access, registration));

        var first  = await store.RevokeByApplicationAsync("applications/client-1");
        var second = await store.RevokeByApplicationAsync("applications/client-1");

        Assert.Equal(2, first);
        Assert.Equal(0, second);
    }

    private static SchemataToken Participant(string subject, string session, string application) {
        return new() {
            Type        = TokenTypes.SessionParticipant,
            Status      = Statuses.Valid,
            Parent      = subject,
            Provider    = TokenTypes.SessionParticipant,
            Key         = $"{session}\u001e{application}",
            SessionId   = session,
            Application = application,
        };
    }

    private static (
        RepositoryTokenStore Store,
        Mock<IRepository<SchemataToken>>    Repository
    ) NewStore(Action<Mock<IRepository<SchemataToken>>>? configure = null, Mock<TimeProvider>? time = null) {
        var repository = new Mock<IRepository<SchemataToken>>();
        // Defaults first: a test's own Begin/commit setups must override the shared fallback.
        SetupTransaction(repository);
        configure?.Invoke(repository);

        return (new(NewProvider(repository.Object), time?.Object ?? TimeProvider.System), repository);
    }

    // Rollback must preserve the injected storage failure instead of failing on an absent transaction.
    private static void SetupTransaction(Mock<IRepository<SchemataToken>> repository) {
        repository.SetReturnsDefault<IUnitOfWork>(new Mock<IUnitOfWork>().Object);
    }

    private static IServiceProvider NewProvider(IRepository<SchemataToken> repository) {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IRepository<SchemataToken>))).Returns(repository);
        return provider.Object;
    }

    // Application-revocation attempts and their CAS classification use separate scopes so an
    // aborted context is disposed before the re-read. Each scope receives the next repository.
    private static Mock<IServiceScopeFactory> NewScopeFactory(
        TimeProvider                              time,
        params Mock<IRepository<SchemataToken>>[] repositories
    ) {
        foreach (var repository in repositories) {
            SetupTransaction(repository);
        }

        var scopes = new Mock<IServiceScopeFactory>();
        var handed = 0;
        scopes.Setup(f => f.CreateScope()).Returns(() => {
            var current  = repositories[handed++];
            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(IRepository<SchemataToken>))).Returns(current.Object);
            provider.Setup(p => p.GetService(typeof(RepositoryTokenStore)))
                    .Returns(new RepositoryTokenStore(provider.Object, time, scopes.Object));
            var scope = new Mock<IServiceScope>();
            scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
            return scope.Object;
        });

        return scopes;
    }

    private static Mock<TimeProvider> NewClock() {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(Now, TimeSpan.Zero));

        return time;
    }

    private static void SetupStoredRows(Mock<IRepository<SchemataToken>> repository, params SchemataToken[] rows) {
        Func<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>,
             CancellationToken,
             ValueTask<bool>> replay =
            (predicate, _) => ValueTask.FromResult(predicate(rows.AsQueryable()).Any());

        repository.Setup(r => r.AnyAsync(
                       It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns(replay);
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
        SetupList(repository, null, rows);
    }

    private static void SetupList(
        Mock<IRepository<SchemataToken>> repository,
        StreamSpy?                       spy,
        params SchemataToken[]           rows
    ) {
        repository.Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataToken>, IQueryable<SchemataToken>> predicate,
                            CancellationToken _) => EnumerateAsync(predicate(rows.AsQueryable()), spy));
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T>(IEnumerable<T> items, StreamSpy? spy = null) {
        try {
            foreach (var item in items) {
                yield return item;
            }
        } finally {
            spy?.MarkDisposed();
        }

        await Task.CompletedTask;
    }

    private sealed class StreamSpy {
        public bool Disposed;
        public bool MutatedWhileOpen;

        public void MarkDisposed() {
            Disposed = true;
        }

        public void ObserveMutation() {
            if (!Disposed) {
                MutatedWhileOpen = true;
            }
        }
    }
}
