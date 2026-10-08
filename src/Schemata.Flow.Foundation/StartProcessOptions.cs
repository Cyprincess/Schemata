namespace Schemata.Flow.Foundation;

/// <summary>Options applied when starting a Flow process instance.</summary>
public sealed class StartProcessOptions
{
    /// <summary>An exact version, or "latest" to select the registry's explicit latest version.</summary>
    public string DefinitionVersion { get; init; } = "1";

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
