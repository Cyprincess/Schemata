using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Schemata.Core.Tests.Common;

public class PredicateShould
{
    [Fact]
    public void True_AlwaysReturnTrue() {
        var predicate = Predicate.True<int>();
        var compiled  = predicate.Compile();

        Assert.True(compiled(0));
        Assert.True(compiled(42));
        Assert.True(compiled(-1));
    }

    [Fact]
    public void False_AlwaysReturnFalse() {
        var predicate = Predicate.False<int>();
        var compiled  = predicate.Compile();

        Assert.False(compiled(0));
        Assert.False(compiled(42));
        Assert.False(compiled(-1));
    }

    [Fact]
    public void And_BothPredicates_CombineWithAndAlso() {
        Expression<Func<int, bool>> left  = x => x > 0;
        Expression<Func<int, bool>> right = x => x < 10;

        var combined = left.And(right);
        var compiled = combined.Compile();

        Assert.True(compiled(5));
        Assert.False(compiled(-1));
        Assert.False(compiled(15));
    }

    [Fact]
    public void And_LeftNull_ReturnRight() {
        Expression<Func<int, bool>>? left  = null;
        Expression<Func<int, bool>>  right = x => x > 0;

        var combined = left.And(right);
        var compiled = combined.Compile();

        Assert.True(compiled(5));
        Assert.False(compiled(-1));
    }

    [Fact]
    public void And_BothNull_ReturnFalse() {
        Expression<Func<int, bool>>? left  = null;
        Expression<Func<int, bool>>? right = null;

        var combined = left.And(right);
        var compiled = combined.Compile();

        Assert.False(compiled(0));
        Assert.False(compiled(42));
    }

    [Fact]
    public void Or_BothPredicates_CombineWithOrElse() {
        Expression<Func<int, bool>> left  = x => x < 0;
        Expression<Func<int, bool>> right = x => x > 10;

        var combined = left.Or(right);
        var compiled = combined.Compile();

        Assert.True(compiled(-5));
        Assert.True(compiled(15));
        Assert.False(compiled(5));
    }

    [Fact]
    public void Or_LeftNull_ReturnRight() {
        Expression<Func<int, bool>>? left  = null;
        Expression<Func<int, bool>>  right = x => x > 0;

        var combined = left.Or(right);
        var compiled = combined.Compile();

        Assert.True(compiled(5));
        Assert.False(compiled(-1));
    }

    [Fact]
    public void Cast_PreserveLogic() {
        Expression<Func<int, bool>> original = x => x > 5;

        var cast     = Predicate.Cast<int, int>(original);
        var compiled = cast!.Compile();

        Assert.True(compiled(10));
        Assert.False(compiled(3));
    }

    [Fact]
    public void Cast_NullPredicate_ReturnNull() {
        var result = Predicate.Cast<int, int>(null);

        Assert.Null(result);
    }
}
