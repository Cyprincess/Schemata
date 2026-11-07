using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

namespace Schemata.Quota.Foundation;

public sealed class SchemataQuotaBuilder(SchemataOptions options, IServiceCollection services)
{
    public SchemataOptions Options { get; } = options;
    public IServiceCollection Services { get; } = services;
}
