using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;
using Schemata.Expressions.Skeleton;
using Xunit;

namespace Schemata.Expressions.Aip.Tests.Compilation;

public class AipCompilerShould
{
    private readonly AipCompiler _compiler = new();

    [Fact]
    public void Compile_CustomComparatorInstances_UseActualBinarySemantics() {
        var template = Assert.IsType<Filter>(AipParser.Filter.Parse("age = 18"));
        var terms = template.Sequences[0].Factors[0].Terms;
        var original = Assert.IsType<Restriction>(terms[0].Simple);
        terms[0] = new Term(default, null, new Restriction(default, original.Comparable,
            (new ConsumerComparator(ExpressionType.GreaterThan), original.Arg!)));
        var greater = _compiler.Compile<Student, bool>(template).Compile();
        terms[0] = new Term(default, null, new Restriction(default, original.Comparable,
            (new ConsumerComparator(ExpressionType.LessThan), original.Arg!)));
        var less = _compiler.Compile<Student, bool>(template).Compile();

        Assert.True(greater(new() { Age = 19 }));
        Assert.False(greater(new() { Age = 17 }));
        Assert.False(less(new() { Age = 19 }));
        Assert.True(less(new() { Age = 17 }));
    }

    [Fact]
    public void Compile_DynamicCustomComparator_RejectsBeforeAndAfterBuiltinCacheWarmup() {
        var builtin = Assert.IsType<Filter>(AipParser.Filter.Parse("age > 314159"));
        var custom = Assert.IsType<Filter>(AipParser.Filter.Parse("age > 314159"));
        var terms = custom.Sequences[0].Factors[0].Terms;
        var original = Assert.IsType<Restriction>(terms[0].Simple);
        terms[0] = new Term(default, null, new Restriction(default, original.Comparable,
            (new ConsumerComparator(ExpressionType.GreaterThan), original.Arg!)));

        var cold = Assert.Throws<ExpressionException>(() =>
            _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(custom));
        var predicate = _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(builtin).Compile();
        Assert.True(predicate(new Dictionary<string, object?> { ["age"] = 314160L }));
        Assert.False(predicate(new Dictionary<string, object?> { ["age"] = 314158L }));
        var warm = Assert.Throws<ExpressionException>(() =>
            _compiler.Compile<IReadOnlyDictionary<string, object?>, bool>(custom));

        Assert.IsType<ParseException>(cold.InnerException);
        Assert.IsType<ParseException>(warm.InnerException);
        Assert.Equal(cold.Message, warm.Message);
    }

    [Fact]
    public void Compile_UnsupportedConsumerSimple_NormalizesParseException() {
        var template = Assert.IsType<Filter>(AipParser.Filter.Parse("age = 18"));
        template.Sequences[0].Factors[0].Terms[0] = new Term(default, null, new UnsupportedSimple());

        var exception = Assert.Throws<ExpressionException>(() => _compiler.Compile<Student, bool>(template));

        Assert.IsType<ParseException>(exception.InnerException);
    }

    private sealed class ConsumerComparator(ExpressionType type) : IBinary
    {
        public ExpressionType? Type => type;
        public TextPosition Position => default;
        public bool IsConstant => true;
    }

    private sealed class UnsupportedSimple : ISimple
    {
        public TextPosition Position => default;
        public bool IsConstant => false;
    }

    [Fact]
    public void Compile_ComparisonAgainstEntityProperty_ReturnsPredicate() {
        var tree       = _compiler.Parse("age = 18");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Age  = 18 }));
        Assert.False(func(new() { Age = 19 }));
    }

    [Fact]
    public void Compile_SnakeCaseField_BindsPascalCaseProperty() {
        var tree       = _compiler.Parse("full_name = 'Alice'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_ExactVariableAndSnakeCaseField_BindsParameterAndPascalCaseProperty() {
        var tree       = _compiler.Parse("studentProfile.full_name = 'Alice'");
        var expression = _compiler.Compile<StudentProfile, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_SnakeCaseVariable_DoesNotBindCamelCaseParameter() {
        var tree = _compiler.Parse("student_profile.full_name = 'Alice'");

        Assert.Throws<ExpressionException>(() => _compiler.Compile<StudentProfile, bool>(tree));
    }

    [Fact]
    public void Compile_BareUnknownTerm_Throws() {
        // Unknown bare terms fail before a vacuous match-all predicate can compile.
        var tree = _compiler.Parse("nonexistent");

        Assert.Throws<ExpressionException>(() => _compiler.Compile<Student, bool>(tree));
    }

    [Fact]
    public void Compile_HasString_UsesContains() {
        var tree       = _compiler.Parse("full_name : 'lic'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_HasList_UsesContains() {
        var tree       = _compiler.Parse("tags : 'red'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Tags  = ["red", "blue"] }));
        Assert.False(func(new() { Tags = ["green"] }));
    }

    [Fact]
    public void Compile_PresenceOnList_RequiresAtLeastOneElement() {
        var tree       = _compiler.Parse("tags : *");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Tags  = ["red"] }));
        Assert.False(func(new() { Tags = [] }));
    }

    [Fact]
    public void Compile_HasDictionary_UsesContainsKey() {
        var tree       = _compiler.Parse("metadata : 'schedule'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Metadata  = new() { ["schedule"] = "daily" } }));
        Assert.False(func(new() { Metadata = new() { ["owner"]    = "ops" } }));
    }

    [Fact]
    public void Compile_CustomFunction_BindsAtCompileTime() {
        var options = new ExpressionCompileOptions { Functions = { ["startsWith"] = new(args => Expression.Call(args[0], nameof(string.StartsWith), null, args[1])) } };

        var tree       = _compiler.Parse("startsWith(full_name, 'Al')");
        var expression = _compiler.Compile<Student, bool>(tree, options);
        var func       = expression.Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = "Bob" }));
    }

    [Fact]
    public void Compile_CustomFunctionCacheKey_UsesFunctionIdentity() {
        var first  = new ExpressionCompileOptions { Functions = { ["matches"] = new(args => Expression.Call(args[0], nameof(string.StartsWith), null, args[1])) } };
        var second = new ExpressionCompileOptions { Functions = { ["matches"] = new(args => Expression.Call(args[0], nameof(string.EndsWith), null, args[1])) } };

        var tree       = _compiler.Parse("matches(full_name, 'ice')");
        var startsWith = _compiler.Compile<Student, bool>(tree, first).Compile();
        var endsWith   = _compiler.Compile<Student, bool>(tree, second).Compile();

        Assert.False(startsWith(new() { FullName = "Alice" }));
        Assert.True(endsWith(new() { FullName    = "Alice" }));
    }

    [Fact]
    public void Compile_SameTree_ReturnsCachedExpressionInstance() {
        var tree   = _compiler.Parse("age = 18");
        var first  = _compiler.Compile<Student, bool>(tree);
        var second = _compiler.Compile<Student, bool>(tree);

        Assert.Same(first, second);
    }

    [Fact]
    public void Compile_DistinctSources_ReturnSeparateExpressionInstances() {
        var first  = _compiler.Compile<Student, bool>(_compiler.Parse("age = 18"));
        var second = _compiler.Compile<Student, bool>(_compiler.Parse("age = 19"));

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Compile_TimestampBuiltIn_ReturnsDateTimeConstant() {
        var tree       = _compiler.Parse("start_time > timestamp('2024-01-02T03:04:05Z')");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { StartTime  = DateTime.Parse("2024-01-02T03:04:06Z").ToUniversalTime() }));
        Assert.False(func(new() { StartTime = DateTime.Parse("2024-01-02T03:04:04Z").ToUniversalTime() }));
    }

    [Fact]
    public void Compile_DurationBuiltIn_ReturnsTimeSpanConstant() {
        var tree       = _compiler.Parse("wait > duration('4h0m0s')");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Wait  = TimeSpan.FromHours(5) }));
        Assert.False(func(new() { Wait = TimeSpan.FromHours(3) }));
    }

    [Fact]
    public void Compile_RfcTimestampLiteral_AgainstDateTimeOffsetField() {
        var tree       = _compiler.Parse("created_at > '2024-01-02T03:04:05Z'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { CreatedAt  = DateTimeOffset.Parse("2024-01-02T03:04:06Z") }));
        Assert.False(func(new() { CreatedAt = DateTimeOffset.Parse("2024-01-02T03:04:04Z") }));
    }

    [Fact]
    public void Compile_DecimalSecondsDuration_AgainstTimeSpanField() {
        var tree       = _compiler.Parse("wait > 1.2s");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Wait  = TimeSpan.FromSeconds(2) }));
        Assert.False(func(new() { Wait = TimeSpan.FromSeconds(1) }));
    }

    [Fact]
    public void Compile_RepeatedHasWithNestedField_TranslatesToEnumerableAny() {
        var tree       = _compiler.Parse("pets.age:18");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.True(func(new() { Pets  = [new() { Age = 18 }] }));
        Assert.False(func(new() { Pets = [new() { Age = 17 }] }));
    }

    [Fact]
    public void Compile_NullMemberChain_SkipsAsNonMatch() {
        var tree       = _compiler.Parse("advisor.full_name = 'Alice'");
        var expression = _compiler.Compile<Student, bool>(tree);
        var func       = expression.Compile();

        Assert.False(func(new() { Advisor = null }));
        Assert.True(func(new() { Advisor  = new() { FullName = "Alice" } }));
    }

    [Fact]
    public void Compile_NullMemberChain_ValueTypeLeaf_SkipsAsNonMatchForBothEqAndNeq() {
        var equalTree    = _compiler.Parse("advisor.age = 0");
        var notEqualTree = _compiler.Parse("advisor.age != 0");
        var equalFunc    = _compiler.Compile<Student, bool>(equalTree).Compile();
        var notEqualFunc = _compiler.Compile<Student, bool>(notEqualTree).Compile();

        // AIP skip-as-nonmatch treats null receiver comparisons as false for both operators,
        // even when the value-type leaf's default would match the literal.
        Assert.False(equalFunc(new() { Advisor    = null }));
        Assert.False(notEqualFunc(new() { Advisor = null }));

        Assert.True(equalFunc(new() { Advisor     = new() { Age = 0 } }));
        Assert.False(notEqualFunc(new() { Advisor = new() { Age = 0 } }));
        Assert.False(equalFunc(new() { Advisor    = new() { Age = 5 } }));
        Assert.True(notEqualFunc(new() { Advisor  = new() { Age = 5 } }));
    }

    [Fact]
    public void Compile_Quoted_Text_Compares_Against_The_Literal() {
        var notEqual = _compiler.Compile<Student, bool>(_compiler.Parse("full_name != 'full_name'")).Compile();
        Assert.True(notEqual(new() { FullName = "Alice" }));
        Assert.False(notEqual(new() { FullName = "full_name" }));

        var equal = _compiler.Compile<Student, bool>(_compiler.Parse("full_name = 'full_name'")).Compile();
        Assert.True(equal(new() { FullName = "full_name" }));
        Assert.False(equal(new() { FullName = "Alice" }));
    }

    [Fact]
    public void Compile_Quoted_Function_Argument_Stays_A_String_Literal() {
        var options = new ExpressionCompileOptions {
            Functions = { ["startsWith"] = new(args => Expression.Call(args[0], nameof(string.StartsWith), null, args[1])) },
        };

        var func = _compiler.Compile<Student, bool>(_compiler.Parse("startsWith(full_name, 'full_name')"), options).Compile();

        Assert.True(func(new() { FullName  = "full_name!" }));
        Assert.False(func(new() { FullName = "Alice" }));
    }

    [Fact]
    public void Compile_Collection_Index_Binds_Int_ElementAt() {
        var func = _compiler.Compile<Student, bool>(_compiler.Parse("tags.0 = 'red'")).Compile();

        Assert.True(func(new() { Tags  = ["red", "blue"] }));
        Assert.False(func(new() { Tags = ["blue", "red"] }));
    }

    [Fact]
    public void Compile_Parser_Direct_Trees_With_Empty_Source_Compile_To_Distinct_Predicates() {
        var first  = Assert.IsType<Filter>(AipParser.Filter.Parse("age = 18"));
        var second = Assert.IsType<Filter>(AipParser.Filter.Parse("age = 19"));

        Assert.Equal(string.Empty, first.Source);
        Assert.Equal(string.Empty, second.Source);

        var firstFunc  = _compiler.Compile<Student, bool>(first).Compile();
        var secondFunc = _compiler.Compile<Student, bool>(second).Compile();

        Assert.True(firstFunc(new() { Age  = 18 }));
        Assert.False(firstFunc(new() { Age = 19 }));
        Assert.False(secondFunc(new() { Age = 18 }));
        Assert.True(secondFunc(new() { Age = 19 }));
    }

    [Fact]
    public void Compile_Structurally_Identical_Trees_Reuse_The_Cached_Expression() {
        var first  = _compiler.Compile<Student, bool>(_compiler.Parse("age = 18"));
        var second = _compiler.Compile<Student, bool>(_compiler.Parse("age=18"));

        Assert.Same(first, second);
    }

    [Fact]
    public void Compile_Tree_Mutated_After_First_Compile_Recompiles() {
        var tree     = _compiler.Parse("age = 18 AND full_name = 'Alice'");
        var original = _compiler.Compile<Student, bool>(tree).Compile();
        Assert.False(original(new() { Age = 18, FullName = "Bob" }));

        ((Filter)tree).Sequences.RemoveAt(1);
        var mutated = _compiler.Compile<Student, bool>(tree).Compile();
        Assert.True(mutated(new() { Age = 18, FullName = "Bob" }));
        Assert.False(mutated(new() { Age = 19, FullName = "Alice" }));
    }

    [Fact]
    public void Compile_WildcardMatch_AgainstNullString_SkipsAsNonMatch() {
        var func = _compiler.Compile<Student, bool>(_compiler.Parse("full_name = 'Ali*'")).Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = null }));
    }

    [Fact]
    public void Compile_HasString_AgainstNullString_SkipsAsNonMatch() {
        var func = _compiler.Compile<Student, bool>(_compiler.Parse("full_name : 'lic'")).Compile();

        Assert.True(func(new() { FullName  = "Alice" }));
        Assert.False(func(new() { FullName = null }));
    }

    [Fact]
    public void Compile_HasList_AgainstNullCollection_SkipsAsNonMatch() {
        var func = _compiler.Compile<Student, bool>(_compiler.Parse("tags : 'red'")).Compile();

        Assert.True(func(new() { Tags = ["red"] }));
        Assert.False(func(new() { Tags = null! }));
    }

    [Fact]
    public void Compile_Presence_OnNonGenericCollectionSubclass_RequiresAtLeastOneElement() {
        var func = _compiler.Compile<Inventory, bool>(_compiler.Parse("quantities : *")).Compile();

        Assert.True(func(new() { Quantities = new IntValues { 1 } }));
        Assert.False(func(new() { Quantities = new IntValues() }));
    }

    [Fact]
    public void Compile_Membership_OnNonGenericCollectionSubclass_UsesElementType() {
        var func = _compiler.Compile<Inventory, bool>(_compiler.Parse("quantities : 1")).Compile();

        Assert.True(func(new() { Quantities  = new IntValues { 1, 2 } }));
        Assert.False(func(new() { Quantities = new IntValues { 3 } }));
    }

    [Fact]
    public void Compile_CollectionIndex_OnNonGenericCollectionSubclass_UsesElementType() {
        var func = _compiler.Compile<Inventory, bool>(_compiler.Parse("quantities.0 = 1")).Compile();

        Assert.True(func(new() { Quantities  = new IntValues { 1, 2 } }));
        Assert.False(func(new() { Quantities = new IntValues { 2 } }));
    }

    #region Nested type: Pet

    private sealed class Pet
    {
        public int Age { get; set; }
    }

    #endregion

    #region Nested type: Student

    private sealed class Student
    {
        public string?                    FullName  { get; set; }
        public int                        Age       { get; set; }
        public DateTime                   StartTime { get; set; }
        public DateTimeOffset             CreatedAt { get; set; }
        public TimeSpan                   Wait      { get; set; }
        public List<string>               Tags      { get; set; } = [];
        public List<Pet>                  Pets      { get; set; } = [];
        public Dictionary<string, string> Metadata  { get; set; } = [];
        public StudentProfile?            Advisor   { get; set; }
    }

    #endregion

    #region Nested type: StudentProfile

    private sealed class StudentProfile
    {
        public string? FullName { get; set; }

        public int Age { get; set; }
    }

    #endregion

    #region Nested type: IntValues

    private sealed class IntValues : List<int> { }

    #endregion

    #region Nested type: Inventory

    private sealed class Inventory
    {
        public IntValues Quantities { get; set; } = [];
    }

    #endregion
}
