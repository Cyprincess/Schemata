using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Built-in advisors perform client lookup, redirect URI validation, PKCE enforcement,
///     scope validation, prompt handling, consent evaluation, and auto-approval.
/// </summary>
public interface IAuthorizeAdvisor<TApplication> : IAdvisor<AuthorizeContext<TApplication>>
    where TApplication : SchemataApplication;
