using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Reads the current count without mutating it - distinct from the shared <see cref="Increment" /> message, which always mutates.</summary>
public sealed record GetCount : IRequest<int>;