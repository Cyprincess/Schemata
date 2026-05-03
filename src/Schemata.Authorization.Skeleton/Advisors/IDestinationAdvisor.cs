using System.Collections.Generic;
using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Determines which token destinations a claim should be sent to.
///     Advisors add matching destination identifiers (e.g. <c>"access_token"</c>, <c>"id_token"</c>)
///     to the provided set.
/// </summary>
/// <remarks>
///     <c>Handle</c> and <c>Continue</c> results are equivalent: both mean destinations were added
///     to the set. <c>Handle</c> short-circuits remaining advisors; <c>Continue</c> lets the next
///     advisor try. <c>Block</c> excludes the claim from all destinations.
/// </remarks>
public interface IDestinationAdvisor : IAdvisor<Claim, HashSet<string>, ClaimsPrincipal>;
