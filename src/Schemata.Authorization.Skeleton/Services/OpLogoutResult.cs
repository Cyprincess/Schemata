using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>The outcome of a completed logout.</summary>
/// <param name="FrontChannelUris">Prepared front-channel logout URIs to render as hidden iframes.</param>
public sealed record OpLogoutResult(IReadOnlyList<string> FrontChannelUris);