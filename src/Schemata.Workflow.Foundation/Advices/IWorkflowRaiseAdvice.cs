using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advices;

public interface IWorkflowRaiseAdvice : IAdvice<SchemataWorkflow, IEvent, HttpContext>;
