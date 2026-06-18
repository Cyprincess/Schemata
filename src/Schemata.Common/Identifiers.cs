using System;

namespace Schemata.Common;

/// <summary>
///     Purpose-grouped identifier generation. Every identifier the framework persists or
///     emits at runtime should pick its generator from here so call sites are self-documenting
///     and so encoding choices are consistent across subsystems.
/// </summary>
public static class Identifiers
{
    /// <summary>
    ///     Allocates a fresh entity primary key (<c>IIdentifier.Uid</c>).
    /// </summary>
    public static Guid NewUid() {
#if NET10_0_OR_GREATER
        return Guid.CreateVersion7();
#else
        return Guid.NewGuid();
#endif
    }
}
