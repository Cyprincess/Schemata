using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Response from the interaction endpoint describing the pending authorization.</summary>
public class InteractionResponse
{
    /// <summary>Interaction type, e.g. <c>"consent"</c> or <c>"login"</c>.</summary>
    public string? Type { get; set; }

    /// <summary>Original authorization request parameters driving this interaction.</summary>
    public AuthorizeRequest? Request { get; set; }

    /// <summary>Summary of the requesting application for display on the consent screen.</summary>
    public ApplicationResponse? Application { get; set; }

    /// <summary>Scopes the user is being asked to consent to.</summary>
    public IList<ScopeResponse> Scopes { get; set; } = new List<ScopeResponse>();
}
