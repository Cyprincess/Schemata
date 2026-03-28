using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Runs after code validation, before token issuance. Receives the full code exchange context.
/// </summary>
public interface ICodeExchangeAdvisor<TApplication, TToken> : IAdvisor<CodeExchangeContext<TApplication, TToken>>
    where TApplication : SchemataApplication
    where TToken : SchemataToken;
