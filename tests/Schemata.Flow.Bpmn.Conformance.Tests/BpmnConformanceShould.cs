using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public async Task ExecutesAccordingToVector(string vectorPath) {
        ProcessDefinition definition;
        try {
            definition = BpmnXmlAdapter.Parse(Vectors.AbsolutePath(vectorPath));
        } catch (NotSupportedException ex) {
            FailUncatalogued(PendingReason(ex));
            return;
        }

        try {
            BpmnValidator.Validate(definition);
        } catch (FailedPreconditionException ex) when (IsPendingValidation(ex)) {
            FailUncatalogued(PendingReason(ex));
            return;
        }

        try {
            var snapshot = await ExecuteUntilTerminal(definition);
            Assert.True(TerminalStates.Contains(snapshot.Process.State ?? string.Empty), $"Process ended in '{snapshot.Process.State}'.");
        } catch (NotImplementedException ex) when (IsPhaseMarker(ex)) {
            FailUncatalogued(PendingReason(ex));
        }
    }

    [Theory(DisplayName = "Fast MIWG BPMN vector executes according to supported engine semantics")]
    [Trait(ConformanceTraits.Category, ConformanceTraits.Conformance)]
    [Trait(ConformanceTraits.Speed, "Fast")]
    [MemberData(nameof(Vectors.FastSubset), MemberType = typeof(Vectors))]
    public Task ExecutesAccordingToVectorFast(string vectorPath) { return ExecutesAccordingToVector(vectorPath); }

    private static async Task<ProcessSnapshot> ExecuteUntilTerminal(ProcessDefinition definition) {
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
                FailUncatalogued("Process waits for an external trigger not represented by MIWG XML-only execution.");
            }

            snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, execution, active!.CanonicalName, CancellationToken.None);
        }

        return snapshot;
    }

    private static bool IsPendingValidation(Exception ex) {
        return ex.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("requires", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("outgoing", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPhaseMarker(NotImplementedException ex) {
        return ex.Message.Contains("Phase", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not implemented", StringComparison.OrdinalIgnoreCase);
    }

    private static string PendingReason(Exception ex) { return ex.Message.ReplaceLineEndings(" "); }

    private static void FailUncatalogued(string reason) { throw new InvalidOperationException("Pending catalog missing: " + reason); }
}
