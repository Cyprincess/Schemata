using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Tests;

public class SecurityStoreShould
{
    [Fact]
    public async Task Find_By_A_Blank_Canonical_Name_Returns_Null() {
        var (store, _) = NewStore();

        Assert.Null(await store.FindByCanonicalNameAsync(null));
        Assert.Null(await store.FindByCanonicalNameAsync("  "));
    }

    [Fact]
    public async Task Find_By_Canonical_Name_Matches_Only_The_Row_Carrying_It() {
        var match = new SchemataSecurity { CanonicalName = "securities/s-1" };
        var other = new SchemataSecurity { CanonicalName = "securities/s-2" };
        var (store, _) = NewStore(r => {
            Func<Func<IQueryable<SchemataSecurity>, IQueryable<SchemataSecurity>>,
                 CancellationToken,
                 ValueTask<SchemataSecurity?>> replay =
                (predicate, _) => new(
                    predicate(new[] { match, other }.AsQueryable()).SingleOrDefault());

            r.Setup(x => x.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataSecurity>, IQueryable<SchemataSecurity>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns(replay);
        });

        Assert.Same(match, await store.FindByCanonicalNameAsync("securities/s-1"));
    }

    [Fact]
    public async Task List_By_Parent_Filters_Kind_Usage_And_Status_Through_The_Predicate() {
        var rows = new[] {
            new SchemataSecurity {
                Parent = "applications/a-1", Kind = Kinds.Secret, Usage = Usages.Signing, Status = Statuses.Valid,
            },
            new SchemataSecurity {
                Parent = "applications/a-1", Kind = Kinds.Password, Usage = Usages.Authentication, Status = Statuses.Valid,
            },
            new SchemataSecurity {
                Parent = "applications/a-1", Kind = Kinds.Secret, Usage = Usages.Signing, Status = Statuses.Retired,
            },
            new SchemataSecurity {
                Parent = "applications/a-2", Kind = Kinds.Secret, Usage = Usages.Signing, Status = Statuses.Valid,
            },
        };
        var (store, _) = NewStore(r => SetupList(r, rows));

        var found = new List<SchemataSecurity>();
        await foreach (var security in store.ListByParentAsync(
                           "applications/a-1", Kinds.Secret, Usages.Signing, Statuses.Valid)) {
            found.Add(security);
        }

        Assert.Same(rows[0], Assert.Single(found));
    }

    [Fact]
    public async Task List_By_Parent_Orders_By_Create_Time_Descending_Then_Name() {
        var rows = new[] {
            new SchemataSecurity {
                Parent = "applications/a-1", Name = "c", CreateTime = new DateTime(2026, 1, 1),
            },
            new SchemataSecurity {
                Parent = "applications/a-1", Name = "a", CreateTime = new DateTime(2026, 3, 1),
            },
            new SchemataSecurity {
                Parent = "applications/a-1", Name = "b", CreateTime = new DateTime(2026, 3, 1),
            },
        };
        var (store, _) = NewStore(r => SetupList(r, rows));

        var found = new List<SchemataSecurity>();
        await foreach (var security in store.ListByParentAsync("applications/a-1")) {
            found.Add(security);
        }

        Assert.Equal(new[] { 1, 2, 0 }, found.Select(row => Array.IndexOf(rows, row)));
    }

    [Fact]
    public async Task Create_Stores_A_Secret_Value_Verbatim() {
        var (store, repository) = NewStore();

        var row = new SchemataSecurity { Kind = Kinds.Secret, Value = "plain" };
        await store.CreateAsync(row);

        Assert.Equal("plain", row.Value);
        repository.Verify(
            r => r.AddAsync(It.Is<SchemataSecurity>(s => ReferenceEquals(s, row) && s.Value == "plain"),
                            It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Stores_A_Private_Key_Value_Verbatim() {
        var (store, _) = NewStore();

        var row = new SchemataSecurity { Kind = Kinds.PrivateKey, Value = "pem" };
        await store.CreateAsync(row);

        Assert.Equal("pem", row.Value);
    }

    [Fact]
    public async Task Update_Stores_The_Row_Verbatim() {
        var (store, repository) = NewStore();

        var row = new SchemataSecurity { Kind = Kinds.Secret, Value = "plain" };
        await store.UpdateAsync(row);

        Assert.Equal("plain", row.Value);
        repository.Verify(
            r => r.UpdateAsync(It.Is<SchemataSecurity>(s => ReferenceEquals(s, row) && s.Value == "plain"),
                               It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_Removes_And_Commits() {
        var (store, repository) = NewStore();
        var row = new SchemataSecurity { Name = "s-1" };

        await store.DeleteAsync(row);

        repository.Verify(r => r.RemoveAsync(It.Is<SchemataSecurity>(s => ReferenceEquals(s, row)),
                                             It.IsAny<CancellationToken>()),
                          Times.Once);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        SecurityStore<SchemataSecurity>    Store,
        Mock<IRepository<SchemataSecurity>> Repository
    ) NewStore(Action<Mock<IRepository<SchemataSecurity>>>? configure = null) {
        var repository = new Mock<IRepository<SchemataSecurity>>();
        configure?.Invoke(repository);

        return (new(repository.Object), repository);
    }

    private static void SetupList(Mock<IRepository<SchemataSecurity>> repository, SchemataSecurity[] rows) {
        repository.Setup(r => r.ListAsync(
                       It.IsAny<Func<IQueryable<SchemataSecurity>, IQueryable<SchemataSecurity>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataSecurity>, IQueryable<SchemataSecurity>> predicate,
                            CancellationToken _) => EnumerateAsync(predicate(rows.AsQueryable())));
    }

    private static async IAsyncEnumerable<T> EnumerateAsync<T>(IEnumerable<T> items) {
        foreach (var item in items) {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
