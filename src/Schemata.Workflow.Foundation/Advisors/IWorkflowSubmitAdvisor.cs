using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Advisors;

public interface IWorkflowSubmitAdvisor : IAdvisor<WorkflowRequest<IStateful>, HttpContext>;
