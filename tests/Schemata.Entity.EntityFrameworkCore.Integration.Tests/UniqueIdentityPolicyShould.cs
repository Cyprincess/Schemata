using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
using Schemata.Event.Skeleton.Entities;
using Xunit;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests;

/// <summary>
///     Unit coverage for the EF Core unique-violation identity policy, per issue #42: only an
///     identity the provider actually confirms is exposed — a single candidate names the
///     colliding row, while zero or several candidates leave the conflict ambiguous and expose
///     no name.
/// </summary>
public class UniqueIdentityPolicyShould
{
    [Fact]
    public void Single_Confirmed_Candidate_Exposes_Its_Canonical_Name() {
        var uid  = Guid.NewGuid();
        var name = $"courses/{uid:n}";

        var ex = EfCoreUnitOfWork<TestDbContext>.ClassifyUniqueIdentity(
            new object?[] { new Course { Uid = uid, CanonicalName = name } });

        var info = Assert.Single(ex.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
        Assert.Equal(name, ((Abstractions.Errors.ResourceInfoDetail)info).ResourceName);
    }

    [Fact]
    public void Single_Candidate_Without_Canonical_Name_Exposes_Type_Only() {
        var ex = EfCoreUnitOfWork<TestDbContext>.ClassifyUniqueIdentity(
            new object?[] { new Course { Uid = Guid.NewGuid() } });

        var info = Assert.Single(ex.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
        Assert.Null(((Abstractions.Errors.ResourceInfoDetail)info).ResourceName);
    }

    [Fact]
    public void Multiple_Candidates_Expose_No_Guessed_Identity() {
        var ex = EfCoreUnitOfWork<TestDbContext>.ClassifyUniqueIdentity(new object?[] {
            new Course { Uid = Guid.NewGuid(), CanonicalName = "courses/one" },
            new Course { Uid = Guid.NewGuid(), CanonicalName = "courses/two" },
        });

        Assert.DoesNotContain(ex.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
    }

    [Fact]
    public void Zero_Candidates_Expose_No_Identity() {
        var ex = EfCoreUnitOfWork<TestDbContext>.ClassifyUniqueIdentity(Array.Empty<object?>());

        Assert.DoesNotContain(ex.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
    }

    [Fact]
    public void Single_Null_Candidate_Exposes_No_Identity() {
        var ex = EfCoreUnitOfWork<TestDbContext>.ClassifyUniqueIdentity(new object?[] { null });

        Assert.DoesNotContain(ex.Details ?? [], d => d is Abstractions.Errors.ResourceInfoDetail);
    }

    [Fact]
    public void FrameworkIndexes_AreOrdinary_WhileConsumerIndexes_RemainUnique() {
        var options = new DbContextOptionsBuilder<IndexContext>()
            .UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>()
            .Options;
        using var db = new IndexContext(options);

        var subscription = db.Model.FindEntityType(typeof(SchemataEventSubscription));
        Assert.NotNull(subscription);
        var subscriptionIndex = Assert.Single(subscription.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(SchemataEventSubscription.SubscriptionId)]));
        Assert.False(subscriptionIndex.IsUnique);

        var course = db.Model.FindEntityType(typeof(Course));
        Assert.NotNull(course);
        var courseIndex = Assert.Single(course.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(Course.Title)]));
        Assert.True(courseIndex.IsUnique);
    }

    private sealed class IndexContext(DbContextOptions<IndexContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataEventSubscription>();
            model.Entity<Course>();
        }
    }
}
