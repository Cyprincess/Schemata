using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
/// Advisor interface for the workflow Get pipeline.
/// </summary>
/// <remarks>
/// Implementations are invoked when a workflow is retrieved by identifier.
/// The pipeline receives the <see cref="SchemataWorkflow"/> and <see cref="HttpContext"/>,
/// allowing advisors to perform authorization, filtering, or enrichment before the response is returned.
/// Register implementations via <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.IServiceCollection,Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/>.
/// </remarks>
public interface IWorkflowGetAdvisor : IAdvisor<SchemataWorkflow, HttpContext>;
