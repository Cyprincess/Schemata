using System;
using System.Collections.Generic;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using static Schemata.Abstractions.SchemataResources;

namespace Schemata.Flow.Skeleton.Utilities;

/// <summary>
///     Engine-neutral diagnostic helpers shared by every flow engine implementation.
/// </summary>
/// <remarks>
///     The skeleton exposes the full BPMN AST as the canonical contract. Engines that implement
///     a subset (for example the state-machine engine) reject unsupported nodes at validator and
///     runtime through <see cref="RequiresBpmnEngine" /> so the caller can branch on
///     <c>ErrorInfo.reason</c> and surface "use <c>UseBpmn()</c> instead" guidance. Rejection messages
///     and reasons come from the single <c>SchemataResources</c> source so a client error response
///     never depends on which engine raised the failure.
/// </remarks>
public static class FlowDiagnostics
{
    /// <summary>
    ///     Builds a <see cref="FailedPreconditionException" /> tagged with the
    ///     <c>STATE_MACHINE_REQUIRES_BPMN_ENGINE</c> reason so callers can branch on
    ///     <c>ErrorInfo.reason</c> and surface "switch to <c>UseBpmn()</c>" guidance.
    /// </summary>
    /// <param name="element">The flow element that the current engine cannot execute.</param>
    /// <param name="type">Optional human-readable type label; defaults to the element's CLR type name.</param>
    public static FailedPreconditionException RequiresBpmnEngine(FlowElement element, string? type = null) {
        ArgumentNullException.ThrowIfNull(element);
        return new(
            STATE_MACHINE_REQUIRES_BPMN_ENGINE,
            new Dictionary<string, string?> {
                ["element"] = element.Name,
                ["type"]    = type ?? element.GetType().Name,
            });
    }
}
