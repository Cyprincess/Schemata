using System.Collections.Generic;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Per-resource transport selector. Each call restricts the resource to a transport's endpoints
///     by appending its protocol name; an unconfigured selector leaves the resource exposed on every
///     registered endpoint. The selector only records endpoint names — activating a transport
///     feature is a separate, global concern (<c>MapHttp()</c> / <c>MapGrpc()</c> on the builder).
/// </summary>
public sealed class ResourceEndpointSelector
{
    private readonly List<string> _endpoints = [];

    /// <summary>The selected endpoint protocol names, in selection order.</summary>
    public IReadOnlyList<string> Endpoints => _endpoints;

    /// <summary>Restricts the resource to HTTP endpoints.</summary>
    /// <returns>This selector for chaining.</returns>
    public ResourceEndpointSelector MapHttp() {
        _endpoints.Add(HttpResourceAttribute.Name);
        return this;
    }

    /// <summary>Restricts the resource to gRPC endpoints.</summary>
    /// <returns>This selector for chaining.</returns>
    public ResourceEndpointSelector MapGrpc() {
        _endpoints.Add(GrpcResourceAttribute.Name);
        return this;
    }
}
