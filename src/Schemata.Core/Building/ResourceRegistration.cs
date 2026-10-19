using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Core.Building;

public sealed class ResourceRegistration
{
    internal ResourceRegistration(ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods) {
        Entity = resource.Entity;
        Request = resource.Request;
        Detail = resource.Detail;
        Summary = resource.Summary;
        AuthenticationScheme = resource.AuthenticationScheme;
        DefaultPageSize = resource.DefaultPageSize;
        MaxPageSize = resource.MaxPageSize;
        TotalSize = resource.TotalSize;
        ConfiguredTotalSize = resource.ConfiguredTotalSize;
        var endpoints = (resource.Endpoints ?? resource.Entity.GetCustomAttributes<ResourceEndpointAttributeBase>()
                                                      .Select(attribute => attribute.Endpoint))
            .Distinct(StringComparer.Ordinal).ToArray();
        Endpoints = endpoints.Length == 0 ? null : Array.AsReadOnly(endpoints);
        Operations = resource.Operations is null ? null : Array.AsReadOnly(resource.Operations.Distinct().ToArray());
        Methods = Array.AsReadOnly(methods.Select(method => new ResourceMethodRegistration(method)).ToArray());
    }

    internal ResourceRegistration(ResourceRegistration source, string? scheme, int defaultPageSize, int maxPageSize,
        TotalSizeMode? totalSize, IReadOnlyList<string>? endpoints, IReadOnlyList<Operations>? operations,
        IReadOnlyList<ResourceMethodRegistration> methods) {
        Entity = source.Entity;
        Request = source.Request;
        Detail = source.Detail;
        Summary = source.Summary;
        AuthenticationScheme = scheme;
        DefaultPageSize = defaultPageSize;
        MaxPageSize = maxPageSize;
        TotalSize = totalSize ?? TotalSizeMode.Default;
        ConfiguredTotalSize = totalSize;
        Endpoints = endpoints;
        Operations = operations;
        Methods = methods;
    }

    public Type Entity { get; }
    public Type Request { get; }
    public Type Detail { get; }
    public Type Summary { get; }
    public string? AuthenticationScheme { get; }
    public int DefaultPageSize { get; }
    public int MaxPageSize { get; }
    public TotalSizeMode TotalSize { get; }
    public IReadOnlyList<string>? Endpoints { get; }
    public IReadOnlyList<Operations>? Operations { get; }
    public IReadOnlyList<ResourceMethodRegistration> Methods { get; }
    internal TotalSizeMode? ConfiguredTotalSize { get; }

    internal ResourceRegistration WithMethods(IReadOnlyList<ResourceMethodRegistration> methods) => new(this,
        AuthenticationScheme, DefaultPageSize, MaxPageSize, ConfiguredTotalSize, Endpoints, Operations,
        Array.AsReadOnly(methods.ToArray()));
}

public sealed class ResourceMethodRegistration
{
    public ResourceMethodRegistration(string verb, Type handler, ResourceMethodScope scope = ResourceMethodScope.Instance,
        ResourceHttpMethod method = ResourceHttpMethod.Post) {
        Verb = verb;
        Handler = handler;
        Scope = scope;
        Method = method;
    }

    internal ResourceMethodRegistration(ResourceMethodAttribute method) : this(method.Verb, method.Handler, method.Scope, method.Method) { }

    public string Verb { get; }
    public Type Handler { get; }
    public ResourceMethodScope Scope { get; }
    public ResourceHttpMethod Method { get; }
}
