using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;

namespace Schemata.Core.Building;

/// <summary>
///     Default <see cref="ResourceRegistry" />. Written through <see cref="Add" /> while services are
///     being registered; the first read seals it, so a resource cannot appear after the container is
///     built and every consumer observes the same set. Security activations recorded before the
///     resource package attaches its wiring are replayed when it does, so activation and registration
///     produce the same outcome in any order.
/// </summary>
public sealed class ResourceRegistry
{
    private readonly Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> _methods = [];
    private readonly List<ResourceAttribute>                                      _ordered = [];
    private readonly Dictionary<RuntimeTypeHandle, ResourceAttribute>             _resources = [];

    private readonly List<(IServiceCollection Services, ResourceAttribute Resource, IReadOnlyList<ResourceMethodAttribute> Methods)> _pending = [];

    private IServiceCollection? _services;

    private ResourcePipelineWiring? _wiring;

    private bool _authentication;
    private bool _authorization;

    private bool _sealed;

    public IReadOnlyList<ResourceAttribute> Resources {
        get {
            _sealed = true;
            return _ordered;
        }
    }

    public ResourceAttribute? GetResource(Type entity) {
        _sealed = true;
        return _resources.GetValueOrDefault(entity.TypeHandle);
    }

    public IReadOnlyList<ResourceMethodAttribute> GetMethods(Type entity) {
        _sealed = true;
        return _methods.TryGetValue(entity.TypeHandle, out var methods) ? methods : [];
    }



    /// <summary>
    ///     Attaches the resource package's wiring and hands it everything recorded before it existed.
    ///     Called once, from the resource package's registration entry points.
    /// </summary>
    /// <param name="wiring">The callbacks the resource package supplies.</param>
    public void Attach(ResourcePipelineWiring wiring) {
        if (_wiring is not null) {
            return;
        }

        _wiring = wiring;

        foreach (var (services, resource, methods) in _pending) {
            wiring.RegisterResource(services, resource, methods);
            ApplySecurity(services, resource, methods);
        }

        _pending.Clear();

        if (_authentication) {
            ReplayAuthentication(wiring, _services!);
        }

        if (_authorization) {
            ReplayAuthorization(wiring, _services!);
        }
    }

    /// <summary>
    ///     Registers <paramref name="resource" /> and its custom methods. Registering the same entity
    ///     again merges instead of replacing: a <see langword="null" /> endpoint list on either side
    ///     means "every endpoint" and wins, otherwise the lists union; methods merge by verb.
    /// </summary>
    public void Add(ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods) {
        if (_sealed) {
            throw new InvalidOperationException(
                $"Resource '{resource.Entity.FullName}' cannot be registered after the registry has been read. "
                + "Register every resource while configuring services.");
        }

        var handle = resource.Entity.TypeHandle;
        if (!_resources.TryGetValue(handle, out var existing)) {
            _resources[handle] = resource;
            _ordered.Add(resource);
        } else if (existing.Endpoints is null || resource.Endpoints is null) {
            existing.Endpoints = null;
        } else {
            foreach (var endpoint in resource.Endpoints) {
                if (!existing.Endpoints.Contains(endpoint)) {
                    existing.Endpoints.Add(endpoint);
                }
            }
        }

        if (methods.Count == 0) {
            return;
        }

        if (!_methods.TryGetValue(handle, out var declared)) {
            _methods[handle] = [..methods];
            return;
        }

        var byVerb = new Dictionary<string, ResourceMethodAttribute>(StringComparer.Ordinal);
        foreach (var method in declared) {
            byVerb[method.Verb] = method;
        }

        foreach (var method in methods) {
            byVerb[method.Verb] = method;
        }

        declared.Clear();
        declared.AddRange(byVerb.Values);
    }

    /// <summary>
    ///     Registers one resource and its custom methods, applying the resource package's wiring for
    ///     it: the handler and advisor registration, and the activated security stages. Resources
    ///     registered before the wiring attaches are held and applied when it does.
    /// </summary>
    /// <param name="services">The service collection receiving the registrations.</param>
    /// <param name="resource">The resource descriptor.</param>
    /// <param name="methods">The custom methods declared for the resource.</param>
    public void Register(IServiceCollection services, ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods) {
        _services = services;
        Add(resource, methods);

        if (_wiring is not null) {
            _wiring.RegisterResource(services, resource, methods);
        } else {
            _pending.Add((services, resource, methods));
        }

        ApplySecurity(services, resource, methods);
    }

    /// <summary>
    ///     Activates the authentication security stage for every resource, registered before or after
    ///     this call.
    /// </summary>
    /// <param name="services">The service collection receiving the advisor registrations.</param>
    public void ActivateAuthentication(IServiceCollection services) {
        if (_authentication) {
            return;
        }

        _authentication = true;
        _services = services;
        if (_wiring is not null) {
            ReplayAuthentication(_wiring, services);
        }
    }

    /// <summary>
    ///     Activates the authorization security stage for every resource, registered before or after
    ///     this call.
    /// </summary>
    /// <param name="services">The service collection receiving the advisor registrations.</param>
    public void ActivateAuthorization(IServiceCollection services) {
        if (_authorization) {
            return;
        }

        _authorization = true;
        _services = services;
        if (_wiring is not null) {
            ReplayAuthorization(_wiring, services);
        }
    }

    private void ApplySecurity(IServiceCollection services, ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods) {
        if (_wiring is null) {
            return;
        }

        if (_authentication) {
            _wiring.RegisterAuthentication(services, resource, methods);
        }

        if (_authorization) {
            _wiring.RegisterAuthorization(services, resource, methods);
        }
    }

    private void ReplayAuthentication(ResourcePipelineWiring wiring, IServiceCollection services) {
        foreach (var resource in _ordered) {
            wiring.RegisterAuthentication(services, resource, _methods.GetValueOrDefault(resource.Entity.TypeHandle, []));
        }
    }

    private void ReplayAuthorization(ResourcePipelineWiring wiring, IServiceCollection services) {
        foreach (var resource in _ordered) {
            wiring.RegisterAuthorization(services, resource, _methods.GetValueOrDefault(resource.Entity.TypeHandle, []));
        }

        wiring.RegisterAuthorizationAdvisors(services);
    }
}
