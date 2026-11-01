using System.Collections.Generic;
using Xunit;

namespace Schemata.Expressions.Cel.Tests;

public class CelDurationValueShould
{
    private readonly CelCompiler _compiler = new();

    private object? Evaluate(string source, Dictionary<string, object?> bindings) {
        var tree = _compiler.Parse(source);
        var func = _compiler.Compile<Dictionary<string, object?>, object?>(tree).Compile();
        return func(bindings);
    }

    private bool EvaluateBool(string source, Dictionary<string, object?> bindings) {
        var tree = _compiler.Parse(source);
        var func = _compiler.Compile<Dictionary<string, object?>, bool>(tree).Compile();
        return func(bindings);
    }

    [Fact]
    public void Evaluate_LargeEqualDurations_SubtractToZero() {
        Assert.True(EvaluateBool("duration('10000000000s') - duration('10000000000s') == duration('0s')", new()));
    }

    [Fact]
    public void Evaluate_LargeEqualTimestamps_SubtractToZero() {
        Assert.True(EvaluateBool("timestamp('9999-12-31T23:59:59Z') - timestamp('9999-12-31T23:59:59Z') == duration('0s')", new()));
    }

    [Fact]
    public void Evaluate_LargeDurations_CompareWithoutOverflow() {
        Assert.True(EvaluateBool("duration('10000000000s') < duration('20000000000s')", new()));
        Assert.True(EvaluateBool("duration('20000000000s') > duration('10000000000s')", new()));
        Assert.True(EvaluateBool("duration('-10000000000s') < duration('10000000000s')", new()));
    }

    [Fact]
    public void Evaluate_TimestampDifference_BeyondNanosecondRange_ReturnsError() {
        Assert.IsType<CelError>(Evaluate("timestamp('9999-12-31T23:59:59Z') - timestamp('0001-01-01T00:00:00Z')", new()));
    }

    [Fact]
    public void Evaluate_SubMinuteZoneOffset_ReturnsErrorInsteadOfThrowing() {
        Assert.IsType<CelError>(Evaluate("timestamp('2009-02-13T23:31:30Z').getDate('00:00:01')", new()));
        Assert.IsType<CelError>(Evaluate("timestamp('2009-02-13T23:31:30Z').getDate('-00:00:01')", new()));
    }

    [Fact]
    public void Evaluate_WholeMinuteZoneOffset_ConvertsLocalTime() {
        Assert.Equal(5L, Evaluate("timestamp('2009-02-13T23:31:30Z').getHours('+05:30')", new()));
        Assert.Equal(15L, Evaluate("timestamp('2009-02-13T23:31:30Z').getHours('-08:00')", new()));
    }

    [Fact]
    public void Evaluate_LargeDurationSum_BeyondAcceptedRange_ReturnsError() {
        Assert.IsType<CelError>(Evaluate("duration('200000000000s') + duration('200000000000s')", new()));
    }

    [Theory]
    [InlineData("-1.5s")]
    [InlineData("-0.5s")]
    [InlineData("1.5s")]
    [InlineData("-1s")]
    public void Evaluate_DurationWithString_RoundTripsFractionalSeconds(string text) {
        Assert.Equal(text, Evaluate($"string(duration('{text}'))", new()));
    }

    [Theory]
    [InlineData("'999999999999999999999999s'")]
    [InlineData("'99999999999999999999999999999999999999999999h'")]
    public void Evaluate_OversizedDurationText_ReturnsError(string literal) {
        Assert.IsType<CelError>(Evaluate($"duration({literal})", new()));
    }

    [Fact]
    public void Evaluate_IntegerTimestamp_OutsideDateTimeOffsetRange_ReturnsError() {
        Assert.IsType<CelError>(Evaluate("timestamp(999999999999999)", new()));
    }

    [Fact]
    public void Evaluate_TimestampSelector_WithInvalidZoneOffset_ReturnsError() {
        Assert.IsType<CelError>(Evaluate("timestamp('2009-02-13T23:31:30Z').getDate('+25:00')", new()));
    }

    [Fact]
    public void Evaluate_TimestampSelector_WithUnknownNamedZone_ReturnsError() {
        Assert.IsType<CelError>(Evaluate("timestamp('2009-02-13T23:31:30Z').getDate('Mars/Olympus')", new()));
    }

    [Fact]
    public void Evaluate_TimestampSelectors_WithValidZones_StillResolve() {
        Assert.Equal(14L, Evaluate("timestamp('2009-02-13T23:31:30Z').getDate('Australia/Sydney')", new()));
        Assert.Equal(13L, Evaluate("timestamp('2009-02-13T23:31:30Z').getDayOfMonth('+11:00')", new()));
    }

    [Theory]
    [InlineData("int(-9223372036854775808.0)")]
    [InlineData("int(9223372036854775808.0)")]
    [InlineData("uint(-1.0)")]
    [InlineData("uint(18446744073709551616.0)")]
    public void Evaluate_DoubleIntegerConversion_RejectsReferenceRangeBoundaries(string source) {
        Assert.IsType<CelError>(Evaluate(source, new()));
    }

    [Theory]
    [InlineData("int(-9223372036854774784.0)", -9223372036854774784L)]
    [InlineData("int(9223372036854774784.0)", 9223372036854774784L)]
    [InlineData("uint(18446744073709549568.0)", 18446744073709549568UL)]
    [InlineData("int(-1.5)", -1L)]
    [InlineData("uint(1.5)", 1UL)]
    public void Evaluate_DoubleIntegerConversion_ConvertsInteriorValuesByTruncation(string source, object expected) {
        Assert.Equal(expected, Evaluate(source, new()));
    }
}
