using System.Collections.Generic;
using System.Linq;
using Parlot;
using Xunit;

namespace Schemata.Expressions.Cel.Tests.Conformance;

public class CelSpecShould
{
    [Theory]
    [MemberData(nameof(BasicSelfEvalCases))]
    public void Pass_BasicSelfEvalConformanceVectors(string name, string source, CelSpecValue expected) {
        var compiler = new CelCompiler();
        var tree     = compiler.Parse(source);
        if (expected.Value is CelSpecError error) {
            var ex = Assert.Throws<ParseException>(() => compiler.Compile<object, object?>(tree));
            Assert.Contains(error.Message, ex.Message);
            return;
        }

        var result = name == "self_eval_bound_lookup"
            ? compiler.Compile<BindingContext, object?>(tree).Compile()(new(123))
            : compiler.Compile<object, object?>(tree).Compile()(new());

        Assert.True(EqualValues(expected.Value, result), $"{name}: expected {expected.Value}, got {result}");
    }

    public static IEnumerable<object[]> BasicSelfEvalCases() { return CelSpecLoader.BasicSelfEvalCases(); }

    [Fact]
    public void Loads_ExpectedMinimumSelfEvalCaseCount() {
        const int minimum = 20;
        var       cases   = CelSpecLoader.BasicSelfEvalCases().ToList();
        Assert.True(cases.Count >= minimum,
                    $"Expected at least {minimum} self-eval cases from the CEL spec corpus, got {cases.Count}. "
                  + "The spec submodule may be missing, or the loader regex does not match the textproto layout.");
    }

    private static bool EqualValues(object? expected, object? actual) {
        if (expected is IReadOnlyList<object?> expectedList && actual is IReadOnlyList<object?> actualList) {
            return expectedList.SequenceEqual(actualList);
        }

        if (expected is IReadOnlyDictionary<object, object?> expectedMap
         && actual is IReadOnlyDictionary<object, object?> actualMap) {
            return expectedMap.Count == actualMap.Count
                && expectedMap.All(kv => actualMap.TryGetValue(kv.Key, out var value) && Equals(kv.Value, value));
        }

        if (expected is byte[] expectedBytes && actual is byte[] actualBytes) {
            return expectedBytes.SequenceEqual(actualBytes);
        }

        return Equals(expected, actual);
    }

    #region Nested type: BindingContext

    private sealed record BindingContext(long X);

    #endregion
}
