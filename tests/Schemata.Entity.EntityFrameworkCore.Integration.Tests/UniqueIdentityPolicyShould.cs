using System;
using System.Collections.Generic;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;
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
}
