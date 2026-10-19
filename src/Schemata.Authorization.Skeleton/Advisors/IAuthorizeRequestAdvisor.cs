using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>Normalizes optional authorization request representations before protocol validation.</summary>
public interface IAuthorizeRequestAdvisor<TApplication> : IAdvisor<AuthorizeContext<TApplication>>
    where TApplication : SchemataApplication;
