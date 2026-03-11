using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

public interface IWorkflowRaiseAdvisor : IAdvisor<SchemataWorkflow, IEvent, HttpContext>;
