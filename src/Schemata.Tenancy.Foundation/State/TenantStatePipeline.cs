namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Identifies a native state middleware branch selected by the current
///     <see cref="ITenantStatePipelineProvider" />.
/// </summary>
public enum TenantStatePipeline
{
    /// <summary>Native session middleware (<c>UseSession</c>).</summary>
    Session,

    /// <summary>Native antiforgery middleware (<c>UseAntiforgery</c>).</summary>
    Antiforgery,
}
