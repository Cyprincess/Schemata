using System;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

public sealed record SlowPing(TimeSpan Delay, TaskCompletionSource? Entered = null) : IRequest<string>;