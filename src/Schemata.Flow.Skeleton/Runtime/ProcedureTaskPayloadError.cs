using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Describes a typed procedure task rejected by <see cref="ProcedureTaskPayloadValidator" />:
///     some incoming execution path delivers no typed catch payload, or the delivered types do not
///     match the task's payload type.
/// </summary>
/// <param name="Task">The typed procedure task.</param>
/// <param name="ExpectedPayloadType">The payload type required by the task.</param>
/// <param name="IncomingPayloadTypes">The payload types found on upstream message or signal catches.</param>
public sealed record ProcedureTaskPayloadError(
    ProcedureTaskBase   Task,
    Type                ExpectedPayloadType,
    IReadOnlyList<Type> IncomingPayloadTypes
);