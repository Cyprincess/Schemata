using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
///     Advisor interface for the workflow Raise pipeline.
/// </summary>
public interface IRaiseAdvisor : IAdvisor<SchemataWorkflow, IEvent, ClaimsPrincipal>;
