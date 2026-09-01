using System.Collections.Generic;

namespace Schemata.Messaging.Skeleton;

/// <summary>
///     Explicit carrier for the caller's ambient state, so it can cross a DI scope, thread or
///     process boundary that would otherwise drop it. Captured on the sending side by
///     <see cref="MessageContexts.Capture" /> and rebuilt on the far side by
///     <see cref="IMessageContextPropagator.RestoreAsync" />.
/// </summary>
/// <param name="Items">
///     The flattened ambient state. Holds no reference to a scoped object, which is what makes it
///     safe to hand across the boundary.
/// </param>
public sealed record MessageContext(IReadOnlyDictionary<string, string?> Items);
