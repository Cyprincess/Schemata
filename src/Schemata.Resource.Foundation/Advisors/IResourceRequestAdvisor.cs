using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     The first advisor invoked for every resource operation
///     per <seealso href="https://google.aip.dev/121">AIP-121: Resource-oriented design</seealso>.
///     Return <see cref="AdviseResult.Continue" /> to proceed,
///     <see cref="AdviseResult.Handle" /> to short-circuit with a result stored in the
///     <see cref="AdviceContext" />, or <see cref="AdviseResult.Block" /> to deny silently.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IResourceRequestAdvisor<TEntity> : IAdvisor<ClaimsPrincipal?, Operations>
    where TEntity : class, ICanonicalName;
