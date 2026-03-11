using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

public interface IWorkflowGetAdvisor : IAdvisor<SchemataWorkflow, HttpContext>;
