using System;
using Microsoft.Extensions.Options;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Runtime;

/// <summary>
///     <see cref="IPostConfigureOptions{TOptions}" /> implementation that appends a single delivery
///     mode. Multiple instances accumulate naturally because options post-configure is additive.
/// </summary>
internal sealed class ConfigureRoutingConfiguration : IPostConfigureOptions<EventTypeRegistryConfiguration>
{
    private readonly EventRouting _routing;
    private readonly Type         _type;

    /// <summary>Initializes a post-configure action for a single routing assignment.</summary>
    public ConfigureRoutingConfiguration(Type type, EventRouting routing) {
        _type    = type;
        _routing = routing;
    }

    #region IPostConfigureOptions<EventTypeRegistryConfiguration> Members

    public void PostConfigure(string? name, EventTypeRegistryConfiguration options) {
        options.Routings.Add((_type, _routing));
    }

    #endregion
}
