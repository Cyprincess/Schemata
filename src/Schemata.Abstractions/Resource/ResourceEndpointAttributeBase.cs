using System;

namespace Schemata.Abstractions.Resource;

public abstract class ResourceEndpointAttributeBase : Attribute
{
    public ResourceEndpointAttributeBase(string endpoint) { Endpoint = endpoint; }

    public string Endpoint { get; }
}
