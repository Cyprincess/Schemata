using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Skeleton.Advisors;

/// <summary>
///     Advisor pipeline invoked before <see cref="IScheduledJob.ExecuteAsync" />.
///     May short-circuit via <c>AdviseResult.Handle</c> / <c>Block</c>.
/// </summary>
public interface IJobExecutionAdvisor : IAdvisor<JobContext>;
