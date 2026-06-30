using System;
using System.Collections.Generic;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Outcome of a compensation coordinator run.</summary>
public sealed record CompensationResult
{
    /// <summary>Initializes a new compensation outcome.</summary>
    /// <param name="compensated">Handlers whose compensation invocation returned successfully.</param>
    /// <param name="failed">The handler whose invocation failed, or <see langword="null" /> on full success.</param>
    /// <param name="failureReason">The invocation exception, or <see langword="null" /> on full success.</param>
    public CompensationResult(
        IReadOnlyList<ICompensationHandler> compensated,
        ICompensationHandler?               failed,
        Exception?                          failureReason) {
        Compensated   = compensated;
        Failed        = failed;
        FailureReason = failureReason;
    }

    /// <summary>Handlers whose compensation invocation returned successfully.</summary>
    public IReadOnlyList<ICompensationHandler> Compensated { get; init; }

    /// <summary>The handler whose invocation failed, or <see langword="null" /> on full success.</summary>
    public ICompensationHandler? Failed { get; init; }

    /// <summary>The invocation exception, or <see langword="null" /> on full success.</summary>
    public Exception? FailureReason { get; init; }
}
