using System.Linq.Expressions;
using Xunit;

namespace Schemata.Common.Tests;

public class MemberAccessShould
{
    [Fact]
    public void Resolves_Snake_Case_Segment_To_Pascal_Property() {
        var parameter = Expression.Parameter(typeof(Student));

        var access = MemberAccess.Resolve(parameter, "full_name");

        var member = Assert.IsAssignableFrom<MemberExpression>(access);
        Assert.Equal(nameof(Student.FullName), member.Member.Name);
    }

    [Fact]
    public void Returns_Null_For_Unknown_Segment() {
        var parameter = Expression.Parameter(typeof(Student));

        Assert.Null(MemberAccess.Resolve(parameter, "missing"));
    }

    #region Nested type: Student

    private sealed class Student
    {
        public string FullName { get; set; } = string.Empty;
    }

    #endregion
}
