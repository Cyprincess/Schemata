using Schemata.Abstractions.Advisors;

namespace Schemata.Event.Skeleton.Advisors;

/// <summary>
///     Advisor pipeline invoked after handler dispatch (success, failure, or
///     short-circuit).
/// </summary>
public interface IEventConsumeAdvisor : IAdvisor<EventContext>;
