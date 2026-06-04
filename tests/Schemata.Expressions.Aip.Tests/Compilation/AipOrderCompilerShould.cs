using System.Linq;
using Xunit;

namespace Schemata.Expressions.Aip.Tests.Compilation;

public class AipOrderCompilerShould
{
    [Fact]
    public void CompileOrder_SortsAscendingByDefault() {
        var compiler = new AipOrderCompiler();
        var order    = compiler.CompileOrder<Student>("age");

        var result = order(new[] { new Student { Age = 2 }, new Student { Age = 1 } }.AsQueryable()).ToList();

        Assert.Equal(1, result[0].Age);
        Assert.Equal(2, result[1].Age);
    }

    [Fact]
    public void CompileOrder_SortsDescending() {
        var compiler = new AipOrderCompiler();
        var order    = compiler.CompileOrder<Student>("age desc");

        var result = order(new[] { new Student { Age = 1 }, new Student { Age = 2 } }.AsQueryable()).ToList();

        Assert.Equal(2, result[0].Age);
        Assert.Equal(1, result[1].Age);
    }

    [Fact]
    public void CompileOrder_ThenSortsBySecondField() {
        var compiler = new AipOrderCompiler();
        var order    = compiler.CompileOrder<Student>("grade desc,age asc");

        var result = order(new[] {
                new Student { Age = 2, Grade = 1 }, new Student { Age = 1, Grade = 1 },
                new Student { Age = 3, Grade = 2 },
            }.AsQueryable())
           .ToList();

        Assert.Equal(3, result[0].Age);
        Assert.Equal(1, result[1].Age);
        Assert.Equal(2, result[2].Age);
    }

    #region Nested type: Student

    private sealed class Student
    {
        public int Age   { get; set; }
        public int Grade { get; set; }
    }

    #endregion
}
