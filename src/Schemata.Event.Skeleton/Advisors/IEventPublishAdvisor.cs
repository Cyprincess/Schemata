using Schemata.Abstractions.Advisors;

namespace Schemata.Event.Skeleton.Advisors;

/// <summary>
///     Advisor pipeline invoked before handler dispatch begins; may
///     short-circuit via <c>AdviseResult.Handle</c> / <c>Block</c>.
/// </summary>
public interface IEventPublishAdvisor : IAdvisor<EventContext>;
