using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation.Advisors;

/// <summary>
/// Advisor interface for the workflow Raise pipeline.
/// </summary>
/// <remarks>
/// Implementations are invoked when an event is raised on an existing workflow.
/// The pipeline receives the <see cref="SchemataWorkflow"/>, the <see cref="IEvent"/> being raised,
/// and the <see cref="HttpContext"/>, allowing advisors to authorize or intercept state transitions.
/// Register implementations via <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.IServiceCollection,Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/>.
/// </remarks>
public interface IWorkflowRaiseAdvisor : IAdvisor<SchemataWorkflow, IEvent, HttpContext>;
