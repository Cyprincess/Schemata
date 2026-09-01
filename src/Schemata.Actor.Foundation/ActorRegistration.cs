using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation;

/// <summary>A staged <see cref="SchemataActorBuilder.Register{TActor}" /> call: a route key paired with its spawn recipe.</summary>
/// <param name="ActorType">The route key, matched against <see cref="ActorId.Type" />.</param>
/// <param name="Props">The spawn recipe to register for <paramref name="ActorType" />.</param>
public sealed record ActorRegistration(string ActorType, Props Props);