namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Parameters for querying or completing an interaction at the interaction endpoint.</summary>
public class InteractRequest
{
    /// <summary>Opaque interaction code returned in a previous redirect.</summary>
    public string? Code { get; set; }

    /// <summary>URI identifying the interaction type (e.g. device verification, consent).</summary>
    public string? CodeType { get; set; }
}
