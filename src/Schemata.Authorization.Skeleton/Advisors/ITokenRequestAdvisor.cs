using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors invoked once per token request after client authentication.
///     Built-in advisor checks grant type permission for the authenticated client.
/// </summary>
public interface ITokenRequestAdvisor<TApplication> : IAdvisor<TApplication, TokenRequest>
    where TApplication : SchemataApplication;
