using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors invoked once per token request after client authentication, over the stage's
///     <see cref="TokenRequestContext{TApplication}" />. Built-in advisor checks grant type
///     permission for the authenticated client and validates the DPoP proof.
/// </summary>
/// <typeparam name="TApplication">The application entity type.</typeparam>
public interface ITokenRequestAdvisor<TApplication> : IAdvisor<TokenRequestContext<TApplication>>
    where TApplication : SchemataApplication;
