using System;
using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     The task handler reported that the outcome of an external side effect cannot be determined: the
///     external system supports neither result query nor idempotent re-issue for the forwarded
///     <see cref="FlowTaskContext.RequestId" />. The engine surfaces the report through the process failure
///     path instead of re-executing or skipping the effect; the framework makes no exactly-once claim.
/// </summary>
/// <remarks>
///     Carries the canonical status <c>google.rpc.Code.INTERNAL</c> per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>, with
///     <see cref="SchemataConstants.ErrorReasons.FlowEffectOutcomeUnknown" /> as the
///     <see cref="ErrorInfoDetail.Reason" /> so callers and observers can distinguish an indeterminate
///     external outcome from an ordinary task failure.
/// </remarks>
public sealed class FlowEffectOutcomeUnknownException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="FlowEffectOutcomeUnknownException" />. The en-US-invariant message is
    ///     rendered from <see cref="SchemataResources.FLOW_EFFECT_OUTCOME_UNKNOWN" /> with the named
    ///     arguments in <paramref name="args" />.
    /// </summary>
    /// <param name="args">Named arguments substituted into the template: <c>name</c>, <c>task</c>, <c>request</c>.</param>
    /// <param name="innerException">Internal diagnostic cause; excluded from the error response envelope.</param>
    public FlowEffectOutcomeUnknownException(
        IReadOnlyDictionary<string, string?>? args = null,
        Exception? innerException = null
    ) : base(500, ErrorCodes.Internal, LocalizedMessageFormatter.FormatInvariant(SchemataResources.FLOW_EFFECT_OUTCOME_UNKNOWN, args), innerException) {
        Details = [new ErrorInfoDetail { Reason = ErrorReasons.FlowEffectOutcomeUnknown }];
        AttachMetadata(args);
    }

    protected override string? MessageResourceKey => SchemataResources.FLOW_EFFECT_OUTCOME_UNKNOWN;
}
