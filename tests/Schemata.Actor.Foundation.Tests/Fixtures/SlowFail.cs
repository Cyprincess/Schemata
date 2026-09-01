using System;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

public sealed record SlowFail(TimeSpan Delay, string Message) : IRequest<string>;