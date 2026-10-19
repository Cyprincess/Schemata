namespace Schemata.Core.State;

/// <summary>
///     Validates the composition before the single <see cref="IStatePipelineProvider" /> is created.
///     A state capability registers a validator to require the host boundary its branch depends on;
///     every registered validator runs once, in registration order, and takes the services it
///     inspects as constructor dependencies.
/// </summary>
public interface IStatePipelineValidator
{
    /// <summary>Validates the composition; throwing fails provider creation.</summary>
    void Validate();
}
