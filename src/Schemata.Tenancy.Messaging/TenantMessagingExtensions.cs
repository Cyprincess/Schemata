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
    ///     execution scope through a tenant-bound <see cref="InProcessRequestDispatcher" />. When
    ///     the host's public dispatcher slots are owned by the in-process dispatcher, tenant
    ///     callers resolve that same tenant-bound instance, so tenant-registered handlers and
    ///     pipeline advisors apply. When a transport (e.g. RabbitMQ) owns the host's dispatcher
    ///     slots, tenant callers keep publishing through that transport and only the inbound
    ///     concrete is tenant-bound.
    /// </summary>
    public static SchemataTenancyBuilder<TTenant> UseMessaging<TTenant>(this SchemataTenancyBuilder<TTenant> builder)
        where TTenant : SchemataTenant {
        builder.Services.Replace(ServiceDescriptor.Singleton<IMessageExecutionScopeFactory, TenantMessageExecutionScopeFactory<TTenant>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IMessageContextPropagator, TenantMessageContextPropagator>());
        builder.Services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, services, root) => {
            services.TryAddScoped<InProcessRequestDispatcher>();

            // The host publishes dispatcher ownership at registration time; a transport or
            // application replacement leaves the contract absent and keeps serving tenant callers.
            if (root.GetService<InProcessDispatcherOwnership>() is null) return;

            services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
            services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
            services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        }));
        return builder;
    }
}
