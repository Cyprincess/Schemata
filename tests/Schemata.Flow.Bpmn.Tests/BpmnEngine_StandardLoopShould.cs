using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_StandardLoopShould
{
    [Fact]
    public async Task Execute_TestBeforeTrueWithFalseConditionUpFront_SkipsActivityEntirely() {
        var condition = LoopCounterLessThan(0);
        var result    = await ExecuteAsync(true, condition);

        Assert.Equal("Completed", result.Snapshot.Process.State);
        Assert.Empty(LoopTransitions(result.Transitions));
        Assert.DoesNotContain(result.Transitions, t => t is { Previous: "loop", Posterior: "loop" });
    }

    [Fact]
    public async Task Execute_TestBeforeTrueWithConditionFlippingToFalse_RunsExactNTimes() {
        var condition = LoopCounterLessThan(3);
        var result    = await ExecuteAsync(true, condition);

        var iterations = LoopTransitions(result.Transitions).ToList();
        Assert.Equal(3, iterations.Count);
        Assert.All(iterations, t => Assert.Equal(TransitionKind.Move, t.Kind));
        Assert.Equal(3, LoopCounter(result.Snapshot.Tokens.Single()));
    }

    [Fact]
    public async Task Execute_TestBeforeFalseWithFalseConditionUpFront_RunsExactlyOnce() {
        var condition = LoopCounterLessThan(0);
        var result    = await ExecuteAsync(false, condition);

        var iteration = Assert.Single(LoopTransitions(result.Transitions));
        Assert.Equal(TransitionKind.Move, iteration.Kind);
        Assert.Equal(1, LoopCounter(result.Snapshot.Tokens.Single()));
    }

    [Fact]
    public async Task Execute_TestBeforeFalseWithConditionFlippingToFalse_RunsAtLeastOnce() {
        var condition = LoopCounterLessThan(2);
        var result    = await ExecuteAsync(false, condition);

        var iterations = LoopTransitions(result.Transitions).ToList();
        Assert.Equal(2, iterations.Count);
        Assert.Equal(2, LoopCounter(result.Snapshot.Tokens.Single()));
    }

    [Fact]
    public async Task Execute_WithLoopMaximumSetAndConditionAlwaysTrue_TerminatesAtMaximum() {
        var result = await ExecuteAsync(true, Const(true), 4);

        var iterations = LoopTransitions(result.Transitions).ToList();
        Assert.Equal(4, iterations.Count);
        Assert.Equal(4, LoopCounter(result.Snapshot.Tokens.Single()));
        Assert.Equal("Completed", result.Snapshot.Process.State);
    }

    [Fact]
    public async Task Execute_WithLoopMaximumAndConditionFalseFirst_RespectsCondition() {
        var condition = LoopCounterLessThan(2);
        var result    = await ExecuteAsync(true, condition, 10);

        var iterations = LoopTransitions(result.Transitions).ToList();
        Assert.Equal(2, iterations.Count);
        Assert.Equal(2, LoopCounter(result.Snapshot.Tokens.Single()));
    }

    [Fact]
    public async Task Execute_WhenIterationFails_AbortsLoopAndPropagatesFailure() {
        var condition = new LambdaConditionExpression {
            Lambda = ctx => {
                if (ReadLoopCounter(ctx.Bookkeeping) == 1) {
                    throw new InvalidOperationException("loop boom");
                }

                return new(true);
            },
        };

        var result = await ExecuteAsync(true, condition, 5);

        var iteration = Assert.Single(LoopTransitions(result.Transitions));
        Assert.Equal(TransitionKind.Move, iteration.Kind);
        Assert.Equal("Failed", result.Snapshot.Tokens.Single().State);
        Assert.Equal("Failed", result.Snapshot.Process.State);
        Assert.Equal(1, LoopCounter(result.Snapshot.Tokens.Single()));
        Assert.Contains(result.Transitions, t => t.Kind == TransitionKind.Fail && t.Token == result.Snapshot.Tokens.Single().CanonicalName);
    }

    private static async Task<LoopExecutionResult> ExecuteAsync(
        bool                  testBefore,
        IConditionExpression? condition,
        int?                  loopMaximum = null
    ) {
        var definition = LoopDefinition(testBefore, condition, loopMaximum);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot    = await engine.StartAsync(definition, process, CancellationToken.None);
        var transitions = new List<SchemataProcessTransition>(snapshot.Transitions);

        while (!string.Equals(snapshot.Process.State, "Completed", StringComparison.OrdinalIgnoreCase)
            && snapshot.Tokens.Any(t => string.Equals(t.State, "Active", StringComparison.OrdinalIgnoreCase))) {
            var token = snapshot.Tokens.First(t => string.Equals(t.State, "Active", StringComparison.OrdinalIgnoreCase));
            snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, token.CanonicalName, CancellationToken.None);
            transitions.AddRange(snapshot.Transitions);
        }

        return new(snapshot, transitions);
    }

    private static ProcessDefinition LoopDefinition(
        bool                  testBefore,
        IConditionExpression? condition,
        int?                  loopMaximum
    ) {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var loop     = new NoneTask { Name = "loop" };
        var after    = new NoneTask { Name = "after" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        loop.LoopCharacteristics = new StandardLoopCharacteristics {
            LoopCondition = condition,
            LoopMaximum   = loopMaximum,
            TestBefore    = testBefore,
        };

        return new() {
            Name     = "standard-loop",
            Elements = { start, loop, after, endEvent },
            Flows = {
                new() { Source = start, Target = loop },
                new() { Source = loop, Target = after },
                new() { Source = after, Target = endEvent },
            },
        };
    }

    private static IEnumerable<SchemataProcessTransition> LoopTransitions(IEnumerable<SchemataProcessTransition> transitions) {
        return transitions.Where(t => t is { Kind: TransitionKind.Move, Previous: "loop", Posterior: "loop" });
    }

    private static IConditionExpression LoopCounterLessThan(int maximum) {
        return new LambdaConditionExpression {
            Lambda = ctx => new(ReadLoopCounter(ctx.Bookkeeping) < maximum),
        };
    }

    private static IConditionExpression Const(bool value) {
        return new LambdaConditionExpression { Lambda = _ => new(value) };
    }

    private static int LoopCounter(SchemataProcessToken token) {
        return token.Bookkeeping.TryGetValue("loopCounter", out var value) ? value : 0;
    }

    private static int ReadLoopCounter(IReadOnlyDictionary<string, int> bookkeeping) {
        return bookkeeping.TryGetValue("loopCounter", out var value) ? value : 0;
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private sealed record LoopExecutionResult(
        ProcessSnapshot                         Snapshot,
        IReadOnlyList<SchemataProcessTransition> Transitions);
}
