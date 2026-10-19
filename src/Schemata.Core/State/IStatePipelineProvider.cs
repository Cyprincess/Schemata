using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Schemata.Core.State;

/// <summary>
///     Identifies a native state middleware branch. The branch executed for a
///     request is selected by the current <see cref="IStatePipelineProvider" />.
/// </summary>
public enum StatePipeline
{
    /// <summary>Native session middleware (<c>UseSession</c>).</summary>
    Session,

    /// <summary>Native antiforgery middleware (<c>UseAntiforgery</c>).</summary>
    Antiforgery,
}

/// <summary>
///     Selects the native middleware branch executed for a state pipeline on the
///     current request. The host container registers the global implementation; a
///     more specific request scope registers its own fixed implementation and
///     shadows it. Implementations must not capture request state.
/// </summary>
public interface IStatePipelineProvider
{
    /// <summary>
    ///     Returns the native middleware branch for <paramref name="pipeline" />
    ///     terminating at <paramref name="next" />. Implementations cache the
    ///     branch per (pipeline, next) pair.
    /// </summary>
    /// <param name="pipeline">The state pipeline to branch.</param>
    /// <param name="next">The remaining pipeline the branch terminates into.</param>
    /// <returns>The branch to invoke with the current request.</returns>
    RequestDelegate Get(StatePipeline pipeline, RequestDelegate next);
}

/// <summary>
///     Composes the native state middleware over a service provider with a fresh
///     <see cref="ApplicationBuilder" /> seeded from the startup application
///     properties. The session and antiforgery features register it over the
///     host application services as the global implementation; a state profile
///     registers its own instance over profile services for the same contract.
/// </summary>
public sealed class DefaultStatePipelineProvider : IStatePipelineProvider
{
    private readonly ConcurrentDictionary<(StatePipeline Pipeline, RequestDelegate Next), RequestDelegate> _branches = new();
    private readonly IServiceProvider                                                                       _services;
    private          IReadOnlyDictionary<string, object?>?                                                  _properties;

    /// <summary>
    ///     Initializes the provider over the services the native branches resolve
    ///     their dependencies from.
    /// </summary>
    /// <param name="services">Services visible to the composed native middleware.</param>
    public DefaultStatePipelineProvider(IServiceProvider services) {
        _services = services;
    }

    #region IStatePipelineProvider Members

    /// <inheritdoc />
    public RequestDelegate Get(StatePipeline pipeline, RequestDelegate next) {
        return _branches.GetOrAdd((pipeline, next), Build);
    }

    #endregion

    /// <summary>
    ///     Captures the startup application properties seeded into branch
    ///     builders. The first snapshot wins so every branch of this provider
    ///     observes the same application view.
    /// </summary>
    /// <param name="properties">The application builder properties.</param>
    public void Initialize(IReadOnlyDictionary<string, object?> properties) {
        Interlocked.CompareExchange(ref _properties, new Dictionary<string, object?>(properties), null);
    }

    private RequestDelegate Build((StatePipeline Pipeline, RequestDelegate Next) branch) {
        var builder = new ApplicationBuilder(_services);

        if (_properties is not null) {
            foreach (var (name, value) in _properties) {
                builder.Properties[name] = value;
            }
        }

        switch (branch.Pipeline) {
            case StatePipeline.Session:
                builder.UseSession();
                break;
            case StatePipeline.Antiforgery:
                builder.UseAntiforgery();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(branch), branch.Pipeline, "Unknown state pipeline.");
        }

        builder.Run(branch.Next);

        return builder.Build();
    }
}
