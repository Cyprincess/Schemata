using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     The first advisor invoked for every resource operation
///     per <seealso href="https://google.aip.dev/121">AIP-121: Resource-oriented design</seealso>.
///     The second argument is the operation name as a string ──
///     <c>nameof(Operations.{List/Get/Create/Update/Delete})</c> for CRUD,
///     and the verb in lowerCamelCase (e.g. <c>"run"</c>, <c>"archive"</c>,
///     <c>"batchCreate"</c>) for AIP-136 custom methods.
///     Return <see cref="AdviseResult.Continue" /> to proceed,
///     <see cref="AdviseResult.Handle" /> to short-circuit with a result stored in the
///     <see cref="AdviceContext" />, or <see cref="AdviseResult.Block" /> to deny silently.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IResourceRequestAdvisor<TEntity> : IAdvisor<ClaimsPrincipal?, string>
    where TEntity : class, ICanonicalName;
