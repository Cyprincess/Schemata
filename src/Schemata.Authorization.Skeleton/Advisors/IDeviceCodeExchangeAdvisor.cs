using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Skeleton.Advisors;

/// <summary>
///     Advisors invoked during device code exchange at the token endpoint.
/// </summary>
public interface IDeviceCodeExchangeAdvisor<TApplication> : IAdvisor<DeviceCodeExchangeContext<TApplication>>
    where TApplication : SchemataApplication;
