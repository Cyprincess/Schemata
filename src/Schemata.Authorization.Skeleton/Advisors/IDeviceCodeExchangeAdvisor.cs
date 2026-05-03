using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors invoked during device code exchange at the token endpoint.
/// </summary>
public interface IDeviceCodeExchangeAdvisor<TApplication, TToken> : IAdvisor<DeviceCodeExchangeContext<TApplication, TToken>>
    where TApplication : SchemataApplication
    where TToken : SchemataToken;
