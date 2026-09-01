using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Describes a typed procedure task whose incoming catch payload type does not match its payload type.
/// </summary>
/// <param name="Task">The typed procedure task.</param>
/// <param name="ExpectedPayloadType">The payload type required by the task.</param>
/// <param name="IncomingPayloadTypes">The payload types found on upstream message or signal catches.</param>
public sealed record ProcedureTaskPayloadError(
    ProcedureTaskBase   Task,
    Type                ExpectedPayloadType,
    IReadOnlyList<Type> IncomingPayloadTypes
);