using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Schemata.Tenancy.Foundation.State;

/// <summary>
///     Selects the native middleware branch executed for a state pipeline on the current request.
///     Implementations are bound to one fixed service provider and must not capture request state.
/// </summary>
public interface ITenantStatePipelineProvider
{
    /// <summary>
    ///     Returns the native middleware branch for <paramref name="pipeline" /> terminating at
    ///     <paramref name="next" />, cached per (pipeline, next) pair.
    /// </summary>
    RequestDelegate Get(TenantStatePipeline pipeline, RequestDelegate next);
}

/// <summary>
///     Composes native state middleware over a fixed service provider with a fresh
///     <see cref="ApplicationBuilder" /> seeded from the startup application properties.
/// </summary>
public sealed class DefaultTenantStatePipelineProvider : ITenantStatePipelineProvider
{
    private readonly ConcurrentDictionary<(TenantStatePipeline Pipeline, RequestDelegate Next), RequestDelegate> _branches = new();
    private readonly IServiceProvider                                                                              _services;
    private          IReadOnlyDictionary<string, object?>?                                                         _properties;

    /// <param name="services">Services visible to the composed native middleware.</param>
    public DefaultTenantStatePipelineProvider(IServiceProvider services) {
        _services = services;
    }

    /// <inheritdoc />
    public RequestDelegate Get(TenantStatePipeline pipeline, RequestDelegate next) {
        return _branches.GetOrAdd((pipeline, next), Build);
    }

    /// <summary>Captures one startup application-property snapshot for every branch of this provider.</summary>
    public void Initialize(IReadOnlyDictionary<string, object?> properties) {
        Interlocked.CompareExchange(ref _properties, new Dictionary<string, object?>(properties), null);
    }

    private RequestDelegate Build((TenantStatePipeline Pipeline, RequestDelegate Next) branch) {
        var builder = new ApplicationBuilder(_services);

        if (_properties is not null) {
            foreach (var (name, value) in _properties) {
                builder.Properties[name] = value;
            }
        }

        switch (branch.Pipeline) {
            case TenantStatePipeline.Session:
                builder.UseSession();
                break;
            case TenantStatePipeline.Antiforgery:
                builder.UseAntiforgery();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(branch), branch.Pipeline, "Unknown tenant state pipeline.");
        }

        builder.Run(branch.Next);

        return builder.Build();
    }
}
