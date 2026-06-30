namespace Schemata.Flow.Foundation;

/// <summary>Options applied when starting a Flow process instance.</summary>
public sealed class StartProcessOptions
{
    /// <summary>Copied onto the process row's display name at start.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Copied onto the process row's description at start.</summary>
    public string? Description { get; init; }
}
