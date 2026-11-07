namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Validates a tenant state composition before its <see cref="ITenantStatePipelineProvider" /> is created.
/// </summary>
public interface ITenantStatePipelineValidator
{
    /// <summary>Validates the composition; throwing fails provider creation.</summary>
    void Validate();
}
