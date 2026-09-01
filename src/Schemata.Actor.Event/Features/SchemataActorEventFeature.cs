using Schemata.Actor.Foundation.Features;
using Schemata.Core.Features;
using Schemata.Event.Foundation.Features;

namespace Schemata.Actor.Event.Features;

/// <summary>
///     Marks the Actor.Event bridge as installed and pulls in its two prerequisite features. All
///     actual wiring is per closed event type, done by <c>SchemataActorBuilder.RouteEvent</c> as each
///     route is registered - this feature has nothing to configure up front.
/// </summary>
[DependsOn<SchemataActorFeature>]
[DependsOn<SchemataEventFeature>]
public sealed class SchemataActorEventFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Actor.Event feature.</summary>
    public const int DefaultPriority = SchemataActorFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;
}
