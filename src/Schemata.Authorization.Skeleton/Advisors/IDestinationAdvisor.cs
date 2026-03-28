using System.Collections.Generic;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Determines which token destinations (e.g. access_token, id_token) a claim should be sent to.
///     Advisors add matching destination identifiers to the provided set.
/// </summary>
/// <remarks>
///     Handle and Continue are equivalent for the caller -- both mean "destinations are in the set."
///     Handle short-circuits remaining advisors; Continue lets the next advisor try.
///     Block means the claim should be excluded from all token destinations.
/// </remarks>
public interface IDestinationAdvisor : IAdvisor<Claim, HashSet<string>, ClaimsPrincipal>;
