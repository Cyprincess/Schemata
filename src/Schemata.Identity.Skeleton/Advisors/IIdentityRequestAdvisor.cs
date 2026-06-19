using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises an identity request model with the requested operation and caller principal.
/// </summary>
/// <typeparam name="T">The request model type.</typeparam>
public interface IIdentityRequestAdvisor<in T> : IAdvisor<T, IdentityOperation, ClaimsPrincipal>;
