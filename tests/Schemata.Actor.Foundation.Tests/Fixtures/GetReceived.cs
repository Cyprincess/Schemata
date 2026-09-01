using System.Collections.Generic;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

public sealed record GetReceived : IRequest<IReadOnlyList<string>>;