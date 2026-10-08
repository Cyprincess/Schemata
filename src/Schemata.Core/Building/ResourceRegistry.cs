using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Core.Building;

public sealed class ResourceRegistry
{
    private const string RegistryKey = "Schemata.Resource.Registry";
    private static readonly IReadOnlyList<ResourceMethodRegistration> EmptyMethods = Array.AsReadOnly(Array.Empty<ResourceMethodRegistration>());
    private readonly Dictionary<RuntimeTypeHandle, Entry> _resources = [];
    private readonly List<Entry> _ordered = [];
    private readonly HashSet<Type> _authorizedEntities = [];
    private ResourcePipelineWiring? _wiring;
    private IReadOnlyList<ResourceRegistration>? _snapshot;
    private bool _authentication;
    private bool _authorization;
    private bool _createValidation = true;
    private bool _updateValidation = true;
    private bool _freshness = true;

    public static ResourceRegistry GetOrAdd(SchemataOptions schemata, IServiceCollection services) {
        var registry = schemata.Get<ResourceRegistry>(RegistryKey);
        if (registry is null) {
            registry = new();
            schemata.Set(RegistryKey, registry);
        }
        services.TryAddSingleton(registry);
        return registry;
    }

    public IReadOnlyList<ResourceRegistration> Resources {
        get {
            Seal();
            return _snapshot!;
        }
    }

    public ResourceRegistration? GetResource(Type entity) {
        Seal();
        return _resources.GetValueOrDefault(entity.TypeHandle)?.Registration;
    }

    public IReadOnlyList<ResourceMethodRegistration> GetMethods(Type entity) => GetResource(entity)?.Methods
        ?? EmptyMethods;

    public bool CreateValidation => _createValidation;
    public bool UpdateValidation => _updateValidation;
    public bool Freshness => _freshness;

    public void Attach(IServiceCollection services, ResourcePipelineWiring wiring) {
        EnsureMutable();
        if (_wiring is null) {
            var prepared = _ordered.Select(entry => Prepare(entry.Input, wiring)).ToArray();
            _wiring = wiring;
            for (var i = 0; i < _ordered.Count; i++) {
                _ordered[i].Registration = prepared[i];
                foreach (var installation in _ordered[i].Installations) {
                    Install(_ordered[i], installation);
                }
            }
        }
        ReplaySecurity(services);
        _wiring.ApplyStageChoices(services);
    }

    public void Add(ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods) => Merge(resource, methods, null);

    public void Register(IServiceCollection services, ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods)
        => Merge(resource, methods, services);

    private void Merge(ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods, IServiceCollection? services) {
        EnsureMutable(resource.Entity);
        var input = new ResourceRegistration(resource, methods);
        var handle = input.Entity.TypeHandle;
        var existing = _resources.GetValueOrDefault(handle);
        if (existing is not null) input = Merge(existing.Input, input);
        else input = input.WithMethods(MergeMethods([], input.Methods, input.Entity));
        var prepared = _wiring is null ? input : Prepare(input, _wiring);
        if (existing is not null) {
            foreach (var method in prepared.Methods) {
                var installed = existing.Registration.Methods.FirstOrDefault(candidate => candidate.Verb == method.Verb);
                if (installed is not null && (installed.Handler != method.Handler || installed.Scope != method.Scope
                    || installed.Method != method.Method)) throw Conflict(input.Entity, $"method '{method.Verb}'");
            }
        }
        var entry = existing ?? new Entry(input, prepared);
        entry.Input = input;
        entry.Registration = prepared;
        if (existing is null) {
            _resources.Add(handle, entry);
            _ordered.Add(entry);
        }
        if (services is not null) {
            var installation = entry.Installations.FirstOrDefault(item => ReferenceEquals(item.Services, services));
            if (installation is null) {
                installation = new(services);
                entry.Installations.Add(installation);
            }
            installation.Resource = true;
        }
        if (_wiring is not null) {
            foreach (var installation in entry.Installations) Install(entry, installation);
        }
    }

    private static ResourceRegistration Prepare(ResourceRegistration input, ResourcePipelineWiring wiring) =>
        input.WithMethods(MergeMethods([], wiring.PrepareMethods(input), input.Entity));

    private static ResourceRegistration Merge(ResourceRegistration current, ResourceRegistration incoming) {
        if (current.Request != incoming.Request || current.Detail != incoming.Detail || current.Summary != incoming.Summary) {
            throw Conflict(current.Entity, "type roles");
        }
        var scheme = MergeValue(current.AuthenticationScheme, incoming.AuthenticationScheme, current.Entity, "authentication scheme");
        var defaultPage = MergeValue(current.DefaultPageSize == 0 ? null : (int?)current.DefaultPageSize,
            incoming.DefaultPageSize == 0 ? null : (int?)incoming.DefaultPageSize, current.Entity, "default page size") ?? 0;
        var maxPage = MergeValue(current.MaxPageSize == 0 ? null : (int?)current.MaxPageSize,
            incoming.MaxPageSize == 0 ? null : (int?)incoming.MaxPageSize, current.Entity, "maximum page size") ?? 0;
        var total = MergeValue(current.ConfiguredTotalSize, incoming.ConfiguredTotalSize, current.Entity, "total size policy");
        var operations = current.Operations ?? incoming.Operations;
        if (current.Operations is not null && incoming.Operations is not null
            && !current.Operations.ToHashSet().SetEquals(incoming.Operations)) {
            throw Conflict(current.Entity, "operations");
        }
        IReadOnlyList<string>? endpoints = current.Endpoints is null || incoming.Endpoints is null ? null
            : Array.AsReadOnly(current.Endpoints.Concat(incoming.Endpoints).Distinct(StringComparer.Ordinal).ToArray());
        return new(current, scheme, defaultPage, maxPage, total, endpoints, operations,
            MergeMethods(current.Methods, incoming.Methods, current.Entity));
    }

    private static T? MergeValue<T>(T? current, T? incoming, Type entity, string field) where T : struct {
        if (current is not null && incoming is not null && !EqualityComparer<T>.Default.Equals(current.Value, incoming.Value)) {
            throw Conflict(entity, field);
        }
        return current ?? incoming;
    }

    private static string? MergeValue(string? current, string? incoming, Type entity, string field) {
        if (current is not null && incoming is not null && !string.Equals(current, incoming, StringComparison.Ordinal)) {
            throw Conflict(entity, field);
        }
        return current ?? incoming;
    }

    private static IReadOnlyList<ResourceMethodRegistration> MergeMethods(IReadOnlyList<ResourceMethodRegistration> current,
        IReadOnlyList<ResourceMethodRegistration> incoming, Type entity) {
        var methods = new Dictionary<string, ResourceMethodRegistration>(StringComparer.Ordinal);
        foreach (var method in current.Concat(incoming)) {
            if (methods.TryGetValue(method.Verb, out var existing)) {
                if (existing.Handler != method.Handler || existing.Scope != method.Scope || existing.Method != method.Method) {
                    throw Conflict(entity, $"method '{method.Verb}'");
                }
            } else methods.Add(method.Verb, method);
        }
        return Array.AsReadOnly(methods.Values.ToArray());
    }

    private static InvalidOperationException Conflict(Type entity, string field) =>
        new($"Resource '{entity.FullName}' has conflicting {field} registrations.");

    private void Install(Entry entry, Installation installation) {
        var resource = entry.Registration;
        if (installation.Resource && !installation.Standard) {
            _wiring!.RegisterResource(installation.Services, resource);
            installation.Standard = true;
        }
        if (!ReferenceEquals(installation.Registration, resource)) {
            var closures = resource.Methods.Select(method => _wiring!.MethodClosure(resource, method)).ToHashSet();
            foreach (var closure in installation.Methods.Keys.ToArray()) {
                if (closures.Contains(closure)) continue;
                var removed = installation.Methods[closure];
                installation.Methods.Remove(closure);
                foreach (var descriptor in removed.Descriptors) {
                    var remaining = _ordered.SelectMany(item => item.Installations)
                        .Where(item => ReferenceEquals(item.Services, installation.Services))
                        .SelectMany(item => item.Methods.Values)
                        .FirstOrDefault(item => item.Dependencies.Contains(descriptor));
                    if (remaining is null) installation.Services.Remove(descriptor);
                    else remaining.Descriptors.Add(descriptor);
                }
            }
            installation.Registration = resource;
        }
        foreach (var method in resource.Methods) {
            var closure = _wiring!.MethodClosure(resource, method);
            if (!installation.Methods.TryGetValue(closure, out var owned)) {
                owned = new();
                installation.Methods.Add(closure, owned);
            }
            if (!installation.Resource || owned.Installed) continue;
            Capture(installation.Services, owned.Descriptors, () => owned.Dependencies.AddRange(
                _wiring.RegisterMethod(installation.Services, resource, method)));
            owned.Installed = true;
        }
        ApplySecurity(entry, installation);
        _wiring!.ApplyStageChoices(installation.Services);
    }

    private void ApplySecurity(Entry entry, Installation installation) {
        var resource = entry.Registration;
        var authentication = _authentication || resource.AuthenticationScheme is not null;
        var authorization = _authorization || _authorizedEntities.Contains(resource.Entity);
        if (authentication) _wiring!.RegisterAuthentication(installation.Services, resource, []);
        if (authorization) _wiring!.RegisterAuthorization(installation.Services, resource, []);
        foreach (var method in resource.Methods) {
            var closure = _wiring!.MethodClosure(resource, method);
            if (!installation.Methods.TryGetValue(closure, out var owned)) {
                owned = new();
                installation.Methods.Add(closure, owned);
            }
            if (authentication && !owned.Authentication) {
                Capture(installation.Services, owned.Descriptors, () => _wiring.RegisterAuthentication(installation.Services, resource, [method]));
                owned.Authentication = true;
            }
            if (authorization && !owned.Authorization) {
                Capture(installation.Services, owned.Descriptors, () => _wiring.RegisterAuthorization(installation.Services, resource, [method]));
                owned.Authorization = true;
            }
        }
    }

    // Added descriptors are owned; reused descriptors remain dependencies of every method binding.
    private static void Capture(IServiceCollection services, List<ServiceDescriptor> owned, Action register) {
        var start = services.Count;
        register();
        for (var i = start; i < services.Count; i++) owned.Add(services[i]);
    }

    public void ActivateAuthentication(IServiceCollection services) {
        EnsureMutable();
        _authentication = true;
        ReplaySecurity(services);
    }

    public void ActivateAuthorization(IServiceCollection services) {
        EnsureMutable();
        _authorization = true;
        ReplaySecurity(services);
    }

    public void ActivateAuthorization(IServiceCollection services, Type entity) {
        EnsureMutable(entity);
        _authorizedEntities.Add(entity);
        ReplaySecurity(services);
    }

    private void ReplaySecurity(IServiceCollection services) {
        if (_wiring is null) return;
        foreach (var entry in _ordered) {
            var installation = entry.Installations.FirstOrDefault(item => ReferenceEquals(item.Services, services));
            if (installation is null) {
                installation = new(services);
                entry.Installations.Add(installation);
            }
            Adopt(entry, installation);
            ApplySecurity(entry, installation);
        }
    }

    private static void Adopt(Entry entry, Installation target) {
        foreach (var source in entry.Installations) {
            if (ReferenceEquals(source, target)) continue;
            foreach (var (closure, method) in source.Methods) {
                var descriptors = method.Descriptors.Where(target.Services.Contains).ToList();
                var dependencies = method.Dependencies.Where(target.Services.Contains).ToList();
                if (descriptors.Count == 0 && dependencies.Count == 0) continue;
                if (!target.Methods.TryGetValue(closure, out var adopted)) {
                    adopted = new();
                    target.Methods.Add(closure, adopted);
                }
                adopted.Installed |= method.Installed;
                adopted.Authentication |= method.Authentication;
                adopted.Authorization |= method.Authorization;
                foreach (var descriptor in descriptors) {
                    if (!adopted.Descriptors.Contains(descriptor)) adopted.Descriptors.Add(descriptor);
                }
                foreach (var dependency in dependencies) {
                    if (!adopted.Dependencies.Contains(dependency)) adopted.Dependencies.Add(dependency);
                }
                target.Resource |= source.Resource;
                target.Standard |= source.Standard;
                target.Registration = source.Registration;
            }
        }
    }

    public void WithoutCreateValidation(IServiceCollection services) {
        EnsureMutable();
        _createValidation = false;
        _wiring?.ApplyStageChoices(services);
    }

    public void WithoutUpdateValidation(IServiceCollection services) {
        EnsureMutable();
        _updateValidation = false;
        _wiring?.ApplyStageChoices(services);
    }

    public void WithoutFreshness(IServiceCollection services) {
        EnsureMutable();
        _freshness = false;
        _wiring?.ApplyStageChoices(services);
    }

    private void EnsureMutable(Type? entity = null) {
        if (_snapshot is not null) throw new InvalidOperationException(
            $"Resource '{entity?.FullName ?? "registry"}' cannot be configured after the registry has been read. Register every resource while configuring services.");
    }

    private void Seal() {
        _snapshot ??= Array.AsReadOnly(_ordered.Select(entry => entry.Registration).ToArray());
    }

    private sealed class Entry(ResourceRegistration input, ResourceRegistration registration)
    {
        public ResourceRegistration Input = input;
        public ResourceRegistration Registration = registration;
        public List<Installation> Installations { get; } = [];
    }

    private sealed class Installation(IServiceCollection services)
    {
        public IServiceCollection Services { get; } = services;
        public bool Standard;
        public bool Resource;
        public ResourceRegistration? Registration;
        public Dictionary<(Type Request, Type Response), MethodInstallation> Methods { get; } = [];
    }

    private sealed class MethodInstallation
    {
        public List<ServiceDescriptor> Descriptors { get; } = [];
        public List<ServiceDescriptor> Dependencies { get; } = [];
        public bool Installed;
        public bool Authentication;
        public bool Authorization;
    }
}
