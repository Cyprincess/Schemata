using System.Linq;
using Xunit;

namespace Schemata.Expressions.Order.Tests;

public class OrderCompilerShould
{
    [Fact]
    public void CompileOrder_SortsAscendingByDefault() {
        var compiler = new OrderCompiler();
        var order    = compiler.CompileOrder<Student>("age");

        var result = order(new[] { new Student { Age = 2 }, new Student { Age = 1 } }.AsQueryable()).ToList();

        Assert.Equal(1, result[0].Age);
        Assert.Equal(2, result[1].Age);
    }

    [Fact]
    public void CompileOrder_SortsDescending() {
        var compiler = new OrderCompiler();
        var order    = compiler.CompileOrder<Student>("age desc");

        var result = order(new[] { new Student { Age = 1 }, new Student { Age = 2 } }.AsQueryable()).ToList();

        Assert.Equal(2, result[0].Age);
        Assert.Equal(1, result[1].Age);
    }

    [Fact]
    public void CompileOrder_ThenSortsBySecondField() {
        var compiler = new OrderCompiler();
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

    [Fact]
    public void CompileOrder_ResolvesSnakeCaseField() {
        var compiler = new OrderCompiler();
        var order    = compiler.CompileOrder<Student>("full_name");

        var result = order(new[] { new Student { FullName = "B" }, new Student { FullName = "A" } }.AsQueryable())
           .ToList();

        Assert.Equal("A", result[0].FullName);
        Assert.Equal("B", result[1].FullName);
    }

    [Theory]
    [InlineData("a", new[] { "a" }, new[] { false })]
    [InlineData("a DESC", new[] { "a" }, new[] { true })]
    [InlineData("a asc", new[] { "a" }, new[] { false })]
    [InlineData("a,b", new[] { "a", "b" }, new[] { false, false })]
    [InlineData("a DESC,b ASC", new[] { "a", "b" }, new[] { true, false })]
    public void Parse_ReturnsExpectedKeys(string input, string[] fields, bool[] descending) {
        var keys = new OrderCompiler().Parse(input);

        Assert.Equal(fields.Length, keys.Count);
        for (var i = 0; i < fields.Length; i++) {
            Assert.Equal(fields[i], Assert.Single(keys[i].Path));
            Assert.Equal(descending[i], keys[i].Descending);
        }
    }

    [Fact]
    public void Parse_NestedField_PreservesPathSegments() {
        var keys = new OrderCompiler().Parse("foo.bar desc");

        var key = Assert.Single(keys);
        Assert.Equal(["foo", "bar"], key.Path);
        Assert.True(key.Descending);
    }

    #region Nested type: Student

    private sealed class Student
    {
        public int    Age      { get; set; }
        public int    Grade    { get; set; }
        public string FullName { get; set; } = string.Empty;
    }

    #endregion
}
