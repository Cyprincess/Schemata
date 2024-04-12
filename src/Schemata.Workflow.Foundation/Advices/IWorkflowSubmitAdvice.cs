using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Advices;

public interface IWorkflowSubmitAdvice : IAdvice<WorkflowRequest<IStateful>, HttpContext>;
