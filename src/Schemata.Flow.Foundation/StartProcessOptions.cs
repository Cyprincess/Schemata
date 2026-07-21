namespace Schemata.Flow.Foundation;

/// <summary>Options applied when starting a Flow process instance.</summary>
public sealed class StartProcessOptions
{
    /// <summary>Copied onto the process row's display name at start.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Copied onto the process row's description at start.</summary>
    public string? Description { get; init; }

    /// <summary>
    ///     Per-source idempotency key. When set, starting a process while another instance of the
    ///     same definition already carries this key in a non-terminal state throws
    ///     <see cref="Schemata.Abstractions.Exceptions.AlreadyExistsException" />; a terminal
    ///     instance with the same key does not block the new start.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
