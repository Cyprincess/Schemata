using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
/// Advisor interface for the workflow Submit pipeline.
/// </summary>
/// <remarks>
/// Implementations are invoked when a new workflow is submitted.
/// The pipeline receives the <see cref="WorkflowRequest{T}"/> and <see cref="HttpContext"/>,
/// allowing advisors to validate the request or enforce authorization before the workflow is created.
/// Register implementations via <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.IServiceCollection,Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/>.
/// </remarks>
public interface IWorkflowSubmitAdvisor : IAdvisor<WorkflowRequest<IStateful>, HttpContext>;
