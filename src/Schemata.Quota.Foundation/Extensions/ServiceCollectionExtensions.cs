using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.Skeleton;
using Schemata.Quota.Foundation.Algorithms;
using Schemata.Quota.Foundation.Handlers;
using Schemata.Quota.Foundation.Jobs;
using Schemata.Quota.Foundation.Runtime;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Control;
using Schemata.Quota.Skeleton.Entities;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchemataQuotaAccounting(this IServiceCollection services) {
        services.AddInProcessRequestDispatcher();
        services.AddAuthorizationCore();
        services.TryAddScoped<IQuotaStore, RepositoryQuotaStore>();
        services.TryAddScoped<QuotaReservationSettlement>();
        services.TryAddScoped<IQuotaAccounting, QuotaAccountingService>();
        services.TryAddScoped<IQuotaSubjectResolver, PrincipalQuotaSubjectResolver>();
        services.TryAddScoped<QuotaControlHandler>();
        services.TryAddScoped<IRequestHandler<ReserveQuotaControlRequest, QuotaReservationView>>(sp => sp.GetRequiredService<QuotaControlHandler>());
        services.TryAddScoped<IRequestHandler<CommitQuotaControlRequest, QuotaReservationView>>(sp => sp.GetRequiredService<QuotaControlHandler>());
        services.TryAddScoped<IRequestHandler<ReleaseQuotaControlRequest, QuotaReservationView>>(sp => sp.GetRequiredService<QuotaControlHandler>());
        services.TryAddScoped<IRequestHandler<ListQuotaLedgerControlRequest, IReadOnlyList<SchemataQuotaLedgerEntry>>>(sp => sp.GetRequiredService<QuotaControlHandler>());
        services.TryAddKeyedScoped<IQuotaAlgorithm, FixedWindowQuotaAlgorithm>(QuotaConstants.Algorithms.FixedWindow);
        services.TryAddKeyedScoped<IQuotaAlgorithm, SlidingWindowQuotaAlgorithm>(QuotaConstants.Algorithms.SlidingWindow);
        services.TryAddKeyedScoped<IQuotaAlgorithm, TokenBucketQuotaAlgorithm>(QuotaConstants.Algorithms.TokenBucket);
        services.TryAddKeyedScoped<IQuotaAlgorithm, ConcurrencyQuotaAlgorithm>(QuotaConstants.Algorithms.Concurrency);
        services.AddScheduledJob<QuotaExpiryJob>();
        return services;
    }
}
