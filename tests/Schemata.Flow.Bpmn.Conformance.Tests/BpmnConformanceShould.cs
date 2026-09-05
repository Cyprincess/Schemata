using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Bpmn.Conformance.Tests.Adapters;
using Schemata.Flow.Bpmn.Conformance.Tests.Traits;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Conformance.Tests;

public class BpmnConformanceShould
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase) {
        "Completed",
        "Cancelled",
        "Failed",
        "Terminated",
        "Compensated",
    };

    [Theory(DisplayName = "MIWG BPMN vector executes according to supported engine semantics")]
    [Trait(ConformanceTraits.Category, ConformanceTraits.Conformance)]
    [Trait(ConformanceTraits.Speed, "Full")]
    [MemberData(nameof(Vectors.AllVectors), MemberType = typeof(Vectors))]
    public async Task Executes_According_To_Vector(string vectorPath) {
        var (outcome, reason) = await TryExecuteVector(vectorPath);
        Assert.True(outcome == Outcome.Terminal, outcome == Outcome.NonTerminal
            ? $"Vector '{vectorPath}' ended in '{reason}'."
            : $"Pending catalog missing: {reason}");
    }

    [Theory(DisplayName = "Fast MIWG BPMN vector executes according to supported engine semantics")]
    [Trait(ConformanceTraits.Category, ConformanceTraits.Conformance)]
    [Trait(ConformanceTraits.Speed, "Fast")]
    [MemberData(nameof(Vectors.FastSubset), MemberType = typeof(Vectors))]
    public Task Executes_According_To_Vector_Fast(string vectorPath) { return Executes_According_To_Vector(vectorPath); }

    [Theory(DisplayName = "Catalogued MIWG BPMN vector stays outside the executable subset")]
    [Trait(ConformanceTraits.Category, ConformanceTraits.Conformance)]
    [Trait(ConformanceTraits.Speed, "Full")]
    [MemberData(nameof(Vectors.PendingVectors), MemberType = typeof(Vectors))]
    public async Task Stays_Outside_Executable_Subset(string vectorPath) {
        var (outcome, _) = await TryExecuteVector(vectorPath);
        Assert.True(outcome != Outcome.Terminal,
            $"Stale pending entry: '{vectorPath}' executes to a terminal state; remove it from PendingCatalog.");
    }

    private enum Outcome
    {
        Terminal,
        Pending,
        NonTerminal,
    }

    private static async Task<(Outcome Outcome, string Reason)> TryExecuteVector(string vectorPath) {
        ProcessDefinition definition;
        try {
            definition = BpmnXmlAdapter.Parse(Vectors.AbsolutePath(vectorPath));
        } catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or XmlException) {
            return (Outcome.Pending, PendingReason(ex));
        }

        try {
            BpmnValidator.Validate(definition);
        } catch (FailedPreconditionException ex) {
            return (Outcome.Pending, PendingReason(ex));
        }

        try {
            var snapshot = await ExecuteUntilTerminal(definition);
            if (snapshot is null) {
                return (Outcome.Pending, "Process waits for an external trigger not represented by MIWG XML-only execution.");
            }

            return TerminalStates.Contains(snapshot.Process.State ?? string.Empty)
                ? (Outcome.Terminal, string.Empty)
                : (Outcome.NonTerminal, snapshot.Process.State ?? string.Empty);
        } catch (NotImplementedException ex) when (IsPhaseMarker(ex)) {
            return (Outcome.Pending, PendingReason(ex));
        }
    }

    private static async Task<ProcessSnapshot?> ExecuteUntilTerminal(ProcessDefinition definition) {
        var engine = new BpmnEngine();
        var process = new SchemataProcess {
            Name           = definition.Name,
            CanonicalName  = $"processes/{definition.Name}",
            DefinitionName = definition.Name,
        };

        var execution = new FlowExecutionContext(
            new Mock<IUnitOfWork>(MockBehavior.Strict).Object,
            new ServiceCollection().BuildServiceProvider());
        var snapshot = await engine.StartAsync(definition, process, execution, CancellationToken.None);
        for (var i = 0; i < 64 && !TerminalStates.Contains(snapshot.Process.State ?? string.Empty); i++) {
            var active = snapshot.Tokens.FirstOrDefault(token => string.Equals(token.State, "Active", StringComparison.OrdinalIgnoreCase));
            if (active is null) {
                return null;
            }

            snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, execution, active.CanonicalName, CancellationToken.None);
        }

        return snapshot;
    }

    private static bool IsPhaseMarker(NotImplementedException ex) {
        return ex.Message.Contains("Phase", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not implemented", StringComparison.OrdinalIgnoreCase);
    }

    private static string PendingReason(Exception ex) { return ex.Message.ReplaceLineEndings(" "); }
}
