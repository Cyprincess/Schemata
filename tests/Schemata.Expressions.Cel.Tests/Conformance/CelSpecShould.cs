using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Schemata.Expressions.Cel.Tests.Conformance;

public class CelSpecShould
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Pass_InScopeConformanceVectors(CelSpecCase test) {
        var compiler = new CelCompiler();
        var tree     = compiler.Parse(test.Expression);
        var func     = compiler.Compile<IReadOnlyDictionary<string, object?>, object?>(tree).Compile();
        var result   = func(test.Bindings);

        if (test.Expected.Value is CelSpecError) {
            Assert.IsType<CelError>(result);
            return;
        }

        Assert.True(EqualValues(test.Expected.Value, result),
                    $"{test.Suite}/{test.Name}: expected {Format(test.Expected.Value)}, got {Format(result)}");
    }

    public static IEnumerable<object[]> Cases() { return CelSpecLoader.Cases(); }

    private static bool EqualValues(object? expected, object? actual) {
        if (expected is double ed && actual is double ad) {
            return double.IsNaN(ed) && double.IsNaN(ad) || ed.Equals(ad);
        }

        if (expected is IReadOnlyList<object?> expectedList && actual is IReadOnlyList<object?> actualList) {
            return expectedList.Count == actualList.Count
                && expectedList.Zip(actualList).All(pair => EqualValues(pair.First, pair.Second));
        }

        if (expected is IReadOnlyDictionary<object, object?> expectedMap
         && actual is IReadOnlyDictionary<object, object?> actualMap) {
            return expectedMap.Count == actualMap.Count
                && expectedMap.All(kv => actualMap.TryGetValue(kv.Key, out var value) && EqualValues(kv.Value, value));
        }

        if (expected is byte[] expectedBytes && actual is byte[] actualBytes) {
            return expectedBytes.SequenceEqual(actualBytes);
        }

        return Equals(expected, actual);
    }

    private static string Format(object? value) {
        return value switch {
            null => "null",
            byte[] bytes => Convert.ToHexString(bytes),
            IReadOnlyList<object?> list => "[" + string.Join(", ", list.Select(Format)) + "]",
            IReadOnlyDictionary<object, object?> map => "{" + string.Join(", ", map.Select(kv => Format(kv.Key) + ": " + Format(kv.Value))) + "}",
            var other => other.ToString() ?? string.Empty,
        };
    }
}
