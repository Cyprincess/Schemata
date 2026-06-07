using Microsoft.Extensions.Options;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation.Internal;

/// <summary>
///     Applies the accumulated <see cref="EventTypeRegistryConfiguration" /> pairs to a fresh
///     <see cref="DefaultEventTypeRegistry" /> instance. Used as the factory for the
///     <see cref="IEventTypeRegistry" /> singleton registration.
/// </summary>
internal static class EventTypeRegistryActivator
{
    public static IEventTypeRegistry Build(IOptions<EventTypeRegistryConfiguration> options) {
        var registry = new DefaultEventTypeRegistry();
        foreach (var (type, name) in options.Value.Registrations) {
            registry.Register(type, name);
        }

        return registry;
    }
}