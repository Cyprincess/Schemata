using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Identity.Skeleton.Advisors;

/// <summary>
///     Advises refresh-token principals before issuing refreshed credentials.
/// </summary>
public interface IIdentityRefreshAdvisor : IAdvisor<ClaimsPrincipal>;
