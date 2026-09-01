namespace Schemata.Actor.Skeleton;

/// <summary>
///     Identifies an actor instance by its registered type and a caller-assigned key, unique
///     within that type.
/// </summary>
/// <param name="Type">
///     The actor type name used to look up the spawning <see cref="Props" /> in
///     <see cref="IActorRegistry" />.
/// </param>
/// <param name="Key">The instance key, unique within <paramref name="Type" />.</param>
public readonly record struct ActorId(string Type, string Key)
{
    /// <summary>Renders this identifier as <c>"{Type}/{Key}"</c>.</summary>
    public override string ToString() => $"{Type}/{Key}";
}
