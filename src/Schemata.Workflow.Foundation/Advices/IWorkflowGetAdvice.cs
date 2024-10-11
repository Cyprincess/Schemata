using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advices;

public interface IWorkflowGetAdvice : IAdvice<SchemataWorkflow, HttpContext>;
