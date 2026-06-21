using Schemata.Abstractions.Advisors;

namespace Schemata.Push.Skeleton.Advisors;

/// <summary>
///     Advisor pipeline invoked before fan-out begins. Returning
///     <see cref="AdviseResult.Block" /> aborts the dispatch before any transport runs;
///     <see cref="AdviseResult.Continue" /> proceeds. Used for routing filters, enrichment,
///     rate limiting, and auditing.
/// </summary>
public interface IPushSendAdvisor : IAdvisor<PushContext>;
