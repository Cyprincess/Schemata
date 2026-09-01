using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Insight.Skeleton.Advisors;

/// <summary>
///     Runs before each source is opened: a source-level hook that may block disallowed sources.
///     Return <see cref="AdviseResult.Block" /> or throw to block the source.
/// </summary>
public interface IInsightSourceAdvisor : IAdvisor<SourceBinding, SourceConfig, ClaimsPrincipal?>;