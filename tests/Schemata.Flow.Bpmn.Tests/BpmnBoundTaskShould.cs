using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Expressions.Cel;
using Schemata.Expressions.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class BpmnBoundTaskShould
{
    [Fact]
    public async Task Real_Cel_Result_Is_Applied_Before_Following_Condition() {
        var task = Script();
        var definition = Definition(task, true);
        var snapshot = await RunAsync(definition, Services());

        var token = Assert.Single(snapshot.Tokens);
        Assert.Equal("approved", token.StateName);
        Assert.Equal("42", token.Annotations["score"]);
    }

    [Fact]
    public async Task Frozen_Function_And_Field_Tables_Keep_Original_Execution() {
        var options = new ExpressionCompileOptions();
        options.Functions["doubleScore"] = new(args => Expression.Multiply(args[0], Expression.Constant(2L)));
        var mapping = new Dictionary<string, string> { ["level"] = "risk_level" };
        var task = Script();
        task.Script = "doubleScore(factor)";
        task.Options = options;
        var rule = Rule();
        rule.Outputs = mapping;
        var definition = Definition(task);
        var next = definition.Elements.Single(element => element.Name == "next");
        definition.Elements.Add(rule);
        definition.Flows.Single(flow => flow.Source == task).Target = rule;
        definition.Flows.Add(new() { Source = rule, Target = next });
        definition.Freeze();
        options.Functions["doubleScore"] = new(args => Expression.Multiply(args[0], Expression.Constant(3L)));
        mapping["level"] = "changed";
        Assert.Throws<NotSupportedException>(() => task.Options!.Functions.Clear());
        Assert.Throws<InvalidOperationException>(() => task.Script = "factor * 3");
        Assert.Throws<InvalidOperationException>(() => rule.Key = "replacement");
        Assert.Throws<InvalidOperationException>(() => rule.Version = "two");
        Assert.Throws<InvalidOperationException>(() => task.Output = (_, _, _) => ValueTask.CompletedTask);
        Assert.Throws<InvalidOperationException>(() => task.Language = "other");
        Assert.Throws<InvalidOperationException>(() => task.Input = (_, _) => ValueTask.FromResult(new ScoreInput()));
        Assert.Throws<InvalidOperationException>(() => rule.Outputs = new Dictionary<string, string>());
        var snapshot = await RunAsync(definition, Services());

        var token = Assert.Single(snapshot.Tokens);
        Assert.Equal("42", token.Annotations["score"]);
        Assert.Equal("high", token.Annotations["risk_level"]);
        Assert.False(token.Annotations.ContainsKey("changed"));
    }

    [Theory]
    [InlineData("language", "FLOW_TASK_LANGUAGE_REQUIRED")]
    [InlineData("unknown", "FLOW_TASK_COMPILER_NOT_FOUND")]
    [InlineData("input", "FLOW_TASK_BINDING_REQUIRED")]
    [InlineData("output", "FLOW_TASK_BINDING_REQUIRED")]
    [InlineData("syntax", "FLOW_TASK_EXPRESSION_INVALID")]
    [InlineData("type", "FLOW_TASK_EXPRESSION_INVALID")]
    public async Task Invalid_Script_Binding_Fails_At_Execution(string failure, string reason) {
        var task = Script();
        switch (failure) {
            case "language": task.Language = null; break;
            case "unknown": task.Language = "unknown"; break;
            case "input": task.Input = null; break;
            case "output": task.Output = null; break;
            case "syntax": task.Script = "'\\q'"; break;
            case "type": task.Script = "'text'"; break;
        }
        var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => RunAsync(Definition(task), Services()));
        AssertReason(reason, error);
        var violations = Assert.Single(error.Details!.OfType<PreconditionFailureDetail>()).Violations;
        Assert.NotNull(violations);
        Assert.Equal("score", Assert.Single(violations).Subject);
    }

    [Theory]
    [InlineData("missing", "undeclared reference to 'missing' (in container '')")]
    [InlineData("numerator / denominator", "divide by zero")]
    public async Task Cel_Error_Value_Stops_Output_Guard_And_Progression(string script, string message) {
        FlowTaskContext? reached = null;
        var output = false;
        var guard = false;
        var task = new ScriptTask<IReadOnlyDictionary<string, object?>, object> {
            Name = "dynamic", Language = "cel", Script = script,
            Input = (context, _) => {
                reached = context;
                return ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> { ["numerator"] = 42L, ["denominator"] = 0L });
            },
            Output = (context, _, _) => { output = true; context.Token.Annotations["result"] = "applied"; return ValueTask.CompletedTask; },
        };
        var definition = Definition(task, true);
        definition.Flows.Single(flow => flow.Condition is not null).Condition = new LambdaConditionExpression {
            Lambda = _ => { guard = true; return ValueTask.FromResult(true); },
        };
        using var services = Services();
        var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => RunAsync(definition, services));

        Assert.NotNull(error.Details);
        var info = Assert.Single(error.Details.OfType<ErrorInfoDetail>());
        Assert.Equal("FLOW_TASK_EXPRESSION_INVALID", info.Reason);
        Assert.Equal("dynamic", info.Metadata!["name"]);
        Assert.Equal("cel", info.Metadata["language"]);
        Assert.Equal("CEL_EVALUATION_ERROR", info.Metadata["expression_reason"]);
        Assert.Equal(message, info.Metadata["expression_message"]);
        var violation = Assert.Single(Assert.Single(error.Details.OfType<PreconditionFailureDetail>()).Violations!);
        Assert.Equal("dynamic", violation.Subject);
        Assert.Equal("script", violation.Type);
        Assert.Equal("Script", violation.Description);
        Assert.False(output);
        Assert.False(guard);
        Assert.NotNull(reached);
        Assert.False(reached.Token.Annotations.ContainsKey("result"));
        Assert.NotEqual("next", reached.Token.StateName);
        Assert.NotEqual("approved", reached.Token.StateName);
    }

    [Fact]
    public async Task Dynamic_Script_Accepts_Application_Object_Without_Error_Marker() {
        var value = new ScoreInput { Factor = 42 };
        object? applied = null;
        var task = new ScriptTask<IReadOnlyDictionary<string, object?>, object> {
            Name = "dynamic", Language = "cel", Script = "value",
            Input = (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?> { ["value"] = value }),
            Output = (_, result, _) => { applied = result; return ValueTask.CompletedTask; },
        };
        using var services = Services();
        var snapshot = await RunAsync(Definition(task), services);

        Assert.Same(value, applied);
        Assert.Equal(42, Assert.IsType<ScoreInput>(applied).Factor);
        Assert.Equal("next", Assert.Single(snapshot.Tokens).StateName);
    }

    [Theory]
    [InlineData("input", "FLOW_TASK_INPUT_INVALID")]
    [InlineData("output", "FLOW_TASK_OUTPUT_INVALID")]
    public async Task Numeric_Binding_Overflow_Preserves_Cause(string binding, string reason) {
        var task = Script();
        if (binding == "input") {
            task.Input = (_, _) => ValueTask.FromResult(new ScoreInput { Factor = checked(Convert.ToInt32(long.MaxValue)) });
        } else {
            task.Output = (_, result, _) => { _ = checked((byte)(result * 100)); return ValueTask.CompletedTask; };
        }
        var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => RunAsync(Definition(task), Services()));
        AssertReason(reason, error);
        Assert.IsType<OverflowException>(error.InnerException);
    }

    [Fact]
    public async Task Keyed_Application_Rule_Maps_Actual_Business_Result() {
        var snapshot = await RunAsync(Definition(Rule()), Services());
        var token = Assert.Single(snapshot.Tokens);
        Assert.Equal("next", token.StateName);
        Assert.Equal("high", token.Annotations["risk_level"]);
        Assert.Equal("42", token.Annotations["evaluated_score"]);
    }

    [Theory]
    [InlineData(RuleNoMatchPolicy.Fail)]
    [InlineData(RuleNoMatchPolicy.Continue)]
    public async Task NoMatch_Policy_Controls_Progression_Without_Applying_Output(RuleNoMatchPolicy policy) {
        var rule = Rule();
        FlowTaskContext? reached = null;
        rule.Input = (context, ct) => { reached = context; ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new ScoreInput { Factor = -1 }); };
        rule.NoMatch = policy;
        if (policy == RuleNoMatchPolicy.Fail) {
            var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => RunAsync(Definition(rule), Services()));
            AssertReason("FLOW_RULE_NO_MATCH", error);
            Assert.NotNull(reached);
            Assert.False(reached.Token.Annotations.ContainsKey("risk_level"));
            Assert.False(reached.Token.Annotations.ContainsKey("evaluated_score"));
            Assert.NotEqual("next", reached.Token.StateName);
        } else {
            var snapshot = await RunAsync(Definition(rule), Services());
            var token = Assert.Single(snapshot.Tokens);
            Assert.Equal("next", token.StateName);
            Assert.False(token.Annotations.ContainsKey("risk_level"));
            Assert.False(token.Annotations.ContainsKey("evaluated_score"));
        }
    }

    [Theory]
    [InlineData("key")]
    [InlineData("version")]
    [InlineData("input")]
    [InlineData("output")]
    public async Task Unknown_Or_Absent_Rule_Binding_Fails_At_Execution(string binding) {
        var rule = Rule();
        switch (binding) {
            case "key": rule.Key = "missing"; break;
            case "version": rule.Version = "missing"; break;
            case "input": rule.Input = null; break;
            case "output": rule.Output = null; break;
        }
        var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => RunAsync(Definition(rule), Services()));
        AssertReason(binding is "input" or "output" ? "FLOW_TASK_BINDING_REQUIRED" : "FLOW_TASK_HANDLER_NOT_FOUND", error);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("scalar")]
    [InlineData("conversion")]
    [InlineData("null")]
    public async Task Invalid_Field_Table_Does_Not_Write_Any_Annotation(string failure) {
        var invalid = new Mock<IConvertible>();
        invalid.Setup(value => value.ToString(It.IsAny<IFormatProvider>())).Throws<FormatException>();
        object? output = failure switch {
            "scalar" => 42L,
            "conversion" => new Dictionary<string, object?> { ["first"] = "valid", ["second"] = invalid.Object },
            "null" => null,
            _ => new Dictionary<string, object?> { ["first"] = "valid" },
        };
        FlowTaskContext? reached = null;
        var rule = new BusinessRuleTask<ScoreInput, object> {
            Name = "invalid", Key = "fields", Version = "one",
            Input = (context, ct) => { reached = context; ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new ScoreInput { Factor = 21 }); },
            Output = (context, _, _) => { context.Token.Annotations["applied"] = "yes"; return ValueTask.CompletedTask; },
            Outputs = new Dictionary<string, string> { ["first"] = "first", ["second"] = "second" },
        };
        using var services = new ServiceCollection().AddKeyedSingleton<IFlowRuleHandler<ScoreInput, object>>(("fields", "one"), new FieldRule(output)).BuildServiceProvider();
        var error = await Assert.ThrowsAsync<FailedPreconditionException>(() => RunAsync(Definition(rule), services));
        AssertReason("FLOW_TASK_OUTPUT_INVALID", error);
        Assert.NotNull(reached);
        Assert.False(reached.Token.Annotations.ContainsKey("first"));
        Assert.False(reached.Token.Annotations.ContainsKey("second"));
        Assert.False(reached.Token.Annotations.ContainsKey("applied"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_During_Binding_Stops_Auto_Progression(bool output) {
        using var cancellation = new CancellationTokenSource();
        var task = Script();
        FlowTaskContext? reached = null;
        task.Input = (context, ct) => {
            reached = context;
            if (!output) cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ScoreInput { Factor = 21 });
        };
        task.Output = (context, _, ct) => { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(Definition(task), Services(), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.NotNull(reached);
        Assert.False(reached.Token.Annotations.ContainsKey("score"));
        Assert.NotEqual("next", reached.Token.StateName);
    }

    [Fact]
    public async Task Application_Rule_Cancellation_Does_Not_Apply_Result_Or_Advance() {
        using var cancellation = new CancellationTokenSource();
        var boundary = new Mock<IRiskAssessment>();
        boundary.Setup(value => value.Classify(42L)).Callback(() => cancellation.Cancel()).Returns("high");
        using var services = new ServiceCollection().AddSingleton(boundary.Object)
            .AddKeyedSingleton<IFlowRuleHandler<ScoreInput, IReadOnlyDictionary<string, object?>>, RiskRule>(("risk", "one"))
            .BuildServiceProvider();
        FlowTaskContext? reached = null;
        var rule = Rule();
        rule.Input = (context, ct) => { reached = context; ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new ScoreInput { Factor = 21 }); };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(Definition(rule), services, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.NotNull(reached);
        Assert.False(reached.Token.Annotations.ContainsKey("risk_level"));
        Assert.False(reached.Token.Annotations.ContainsKey("evaluated_score"));
        Assert.NotEqual("next", reached.Token.StateName);
    }

    private static ScriptTask<ScoreInput, long> Script() => new() {
        Name = "score", Language = "cel", Script = "factor * 2",
        Input = (_, ct) => { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new ScoreInput { Factor = 21 }); },
        Output = (context, result, ct) => { ct.ThrowIfCancellationRequested(); context.Token.Annotations["score"] = result.ToString(System.Globalization.CultureInfo.InvariantCulture); return ValueTask.CompletedTask; },
    };

    private static BusinessRuleTask<ScoreInput, IReadOnlyDictionary<string, object?>> Rule() => new() {
        Name = "risk", Key = "risk", Version = "one",
        Input = (_, ct) => { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new ScoreInput { Factor = 21 }); },
        Output = (context, result, ct) => { ct.ThrowIfCancellationRequested(); context.Token.Annotations["evaluated_score"] = Convert.ToString(result["score"], System.Globalization.CultureInfo.InvariantCulture); return ValueTask.CompletedTask; },
        Outputs = new Dictionary<string, string> { ["level"] = "risk_level" },
    };

    private static ServiceProvider Services() {
        var boundary = new Mock<IRiskAssessment>();
        boundary.Setup(value => value.Classify(42L)).Returns("high");
        return new ServiceCollection().AddCelExpressions().AddSingleton(boundary.Object)
            .AddKeyedSingleton<IFlowRuleHandler<ScoreInput, IReadOnlyDictionary<string, object?>>, RiskRule>(("risk", "one"))
            .BuildServiceProvider();
    }

    private static async Task<ProcessSnapshot> RunAsync(ProcessDefinition definition, IServiceProvider services, CancellationToken ct = default) {
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1", DefinitionName = definition.Name };
        var context = Schemata.Flow.Tests.FlowTestCreation.Context(Mock.Of<IUnitOfWork>(), services);
        return await new BpmnEngine().StartAsync(definition, process, context, ct);
    }

    private static ProcessDefinition Definition(Activity task, bool condition = false) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var next = new UserTask { Name = "next" };
        var approved = new UserTask { Name = "approved" };
        var decision = new ExclusiveGateway { Name = "decision" };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition { Name = "bound-task", Elements = { start, task, next, end }, Flows = { new() { Source = start, Target = task }, new() { Source = task, Target = condition ? decision : next }, new() { Source = next, Target = end } } };
        if (condition) {
            definition.Elements.AddRange([decision, approved]);
            definition.Flows.AddRange([
                new() { Source = decision, Target = approved, Condition = new LambdaConditionExpression { Lambda = context => ValueTask.FromResult(context.TokenEntity!.Annotations.TryGetValue("score", out var score) && score == "42") } },
                new() { Source = decision, Target = next, IsDefault = true },
                new() { Source = approved, Target = end },
            ]);
        }
        return definition;
    }

    private static void AssertReason(string reason, FailedPreconditionException error) => Assert.Equal(reason, Assert.Single(error.Details!.OfType<ErrorInfoDetail>()).Reason);

    public sealed class ScoreInput { public long Factor { get; set; } }
    public interface IRiskAssessment { string Classify(long score); }
    public sealed class RiskRule(IRiskAssessment assessment) : IFlowRuleHandler<ScoreInput, IReadOnlyDictionary<string, object?>>
    {
        public ValueTask<BusinessRuleResult<IReadOnlyDictionary<string, object?>>> EvaluateAsync(ScoreInput input, FlowTaskContext context, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            if (input.Factor < 0) return ValueTask.FromResult(new BusinessRuleResult<IReadOnlyDictionary<string, object?>>(false));
            var score = input.Factor * 2;
            IReadOnlyDictionary<string, object?> result = new Dictionary<string, object?> { ["score"] = score, ["level"] = assessment.Classify(score) };
            return ValueTask.FromResult(new BusinessRuleResult<IReadOnlyDictionary<string, object?>>(true, result));
        }
    }

    private sealed class FieldRule(object? invalidOutput) : IFlowRuleHandler<ScoreInput, object>
    {
        public ValueTask<BusinessRuleResult<object>> EvaluateAsync(ScoreInput input, FlowTaskContext context, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            if (invalidOutput is Dictionary<string, object?> fields) fields["first"] = input.Factor * 2;
            return ValueTask.FromResult(new BusinessRuleResult<object>(true, invalidOutput));
        }
    }
}
