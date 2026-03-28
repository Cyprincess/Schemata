using System.Security.Claims;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Advises on a resource request before any operation-specific logic runs.
/// </summary>
/// <typeparam name="TEntity">The entity type the resource represents.</typeparam>
/// <remarks>
///     Invoked as the first advisor in every resource operation (List, Get, Create, Update, Delete).
///     Return <see cref="AdviseResult.Continue" /> to proceed, <see cref="AdviseResult.Handle" /> to short-circuit
///     with a result stored in the context, or <see cref="AdviseResult.Block" /> to deny the request silently.
/// </remarks>
public interface IResourceRequestAdvisor<TEntity> : IAdvisor<ClaimsPrincipal?, Operations>
    where TEntity : class, ICanonicalName;
