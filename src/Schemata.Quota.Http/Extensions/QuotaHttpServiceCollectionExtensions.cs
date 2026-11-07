using Microsoft.AspNetCore.Builder;
using Schemata.Quota.Http.Controllers;

namespace Microsoft.Extensions.DependencyInjection;

public static class QuotaHttpServiceCollectionExtensions
{
    public static IServiceCollection AddSchemataQuotaHttp(this IServiceCollection services) {
        services.AddControllers();
        services.AddSchemataApplicationPart<QuotaController>();
        services.AddSchemataJsonSerializer(_ => { });
        services.AddSchemataJsonTraits();
        return services;
    }
}
