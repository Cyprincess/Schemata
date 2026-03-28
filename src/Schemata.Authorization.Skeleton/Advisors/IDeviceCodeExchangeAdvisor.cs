using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

public interface IDeviceCodeExchangeAdvisor<TApplication, TToken> : IAdvisor<DeviceCodeExchangeContext<TApplication, TToken>>
    where TApplication : SchemataApplication
    where TToken : SchemataToken;
