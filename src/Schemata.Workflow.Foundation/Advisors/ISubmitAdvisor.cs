using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
///     Advisor interface for the workflow Submit pipeline.
/// </summary>
public interface ISubmitAdvisor : IAdvisor<WorkflowRequest<IStateful>, ClaimsPrincipal>;
