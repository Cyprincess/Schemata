using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors for the token introspection endpoint pipeline,
///     per <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html">RFC 7662: OAuth 2.0 Token Introspection</seealso>
///     .
/// </summary>
public interface IIntrospectionAdvisor<TApplication> : IAdvisor<IntrospectionContext<TApplication>>
    where TApplication : SchemataApplication;
