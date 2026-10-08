using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Messaging;
using Schemata.Tenancy.Skeleton.Entities;

namespace Microsoft.AspNetCore.Builder;

public static class TenantMessagingExtensions
{
    /// <summary>
    ///     Enables tenant-scoped message execution: each consumed request runs inside the tenant's
    ///     execution scope through a tenant-bound <see cref="InProcessRequestDispatcher" />, and when
    ///     the host dispatches in-process the public dispatcher interfaces resolve to that same
    ///     tenant instance, so tenant-registered handlers and pipeline advisors apply. When a
    ///     transport (e.g. RabbitMQ) owns the host's dispatcher interfaces, tenant callers keep
    ///     publishing through that transport and only the inbound concrete is tenant-bound.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseMessaging<TTenant>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant {
        builder.Services.Replace(ServiceDescriptor.Singleton<IMessageExecutionScopeFactory, TenantMessageExecutionScopeFactory<TTenant>>());
        builder.Services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, services, root) => {
            services.TryAddScoped<InProcessRequestDispatcher>();

            // The tenant aliases bind to the tenant concrete only when the host's selected owner is
            // the in-process dispatcher; any other owner (a broker transport or an application
            // replacement) keeps serving tenant callers through the host fallback. The probe resolves
            // the host's actual registration once per tenant container.
            var probe = root.CreateAsyncScope();
            try {
                if (probe.ServiceProvider.GetService<IRequestDispatcher>() is not InProcessRequestDispatcher) {
                    return;
                }
            } finally {
                probe.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
            services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
            services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        }));
        return builder;
    }
}
