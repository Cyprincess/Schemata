using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
///     Advisor interface for the workflow Get pipeline.
/// </summary>
public interface IStatusAdvisor : IAdvisor<SchemataWorkflow, ClaimsPrincipal>;
