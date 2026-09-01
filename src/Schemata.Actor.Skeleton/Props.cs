using System;

namespace Schemata.Actor.Skeleton;

/// <summary>The recipe for constructing an actor instance: its <see cref="IActor" /> type and constructor arguments.</summary>
/// <param name="ActorType">The concrete <see cref="IActor" /> implementation to construct.</param>
/// <param name="Args">
///     The arguments to pass to <paramref name="ActorType" />'s constructor, resolved alongside
///     dependency injection, or <see langword="null" /> for a parameterless construction.
/// </param>
public sealed record Props(Type ActorType, object[]? Args = null);
