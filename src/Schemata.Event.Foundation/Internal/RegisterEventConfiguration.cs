using System;
using Microsoft.Extensions.Options;

namespace Schemata.Event.Foundation.Internal;

/// <summary>
///     <see cref="IPostConfigureOptions{TOptions}" /> implementation that appends a single
///     registration. Multiple instances accumulate naturally because options post-configure is
///     additive.
/// </summary>
internal sealed class RegisterEventConfiguration : IPostConfigureOptions<EventTypeRegistryConfiguration>
{
    private readonly string _name;
    private readonly Type   _type;

    public RegisterEventConfiguration(Type type, string name) {
        _type = type;
        _name = name;
    }

    #region IPostConfigureOptions<EventTypeRegistryConfiguration> Members

    public void PostConfigure(string? name, EventTypeRegistryConfiguration options) {
        options.Registrations.Add((_type, _name));
    }

    #endregion
}