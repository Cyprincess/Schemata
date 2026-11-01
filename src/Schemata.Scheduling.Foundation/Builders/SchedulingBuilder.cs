using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Core.Features;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Scheduling.Foundation.Builders;

/// <summary>Fluent builder for registering scheduled jobs with the Scheduling feature.</summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
public sealed class SchedulingBuilder<TJob, TExecution> : IResourceBuilder
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    private readonly TimeProvider _time;

    /// <summary>Initializes a new <see cref="SchedulingBuilder{TJob,TExecution}" /> bound to the given service collection.</summary>
    /// <param name="schemata">The <see cref="SchemataOptions" />.</param>
    /// <param name="services">The service collection that receives jobs and options.</param>
    /// <param name="time">Clock that anchors relative one-time delays; <c>null</c> uses the system clock.</param>
    public SchedulingBuilder(SchemataOptions schemata, IServiceCollection services, TimeProvider? time = null) {
        Schemata = schemata;
        Services = services;
        _time    = time ?? TimeProvider.System;
        var registrations = Schemata.Get<Dictionary<IResourceBuilder, ResourceSecurityRegistration>>(nameof(ResourceSecurityRegistration)) ?? new();
        Schemata.Set(nameof(ResourceSecurityRegistration), registrations);
        registrations[this] = new(
            services => SchedulingAuthorizationRegistration<TJob, TExecution>.AddSchedulingAuthentication(services),
            services => SchedulingAuthorizationRegistration<TJob, TExecution>.AddSchedulingAuthorization(services),
            scheme => Schemata.Set(SchedulingResourceRegistration<TJob, TExecution>.AuthenticationSchemeKey, scheme));
    }

    public SchemataOptions Schemata { get; }

    /// <summary>Service collection that receives job registrations and scheduler options.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    ///     Adds a feature to the Schemata configuration.
    /// </summary>
    /// <typeparam name="T">The <see cref="ISimpleFeature" /> type.</typeparam>
    public void AddFeature<T>()
        where T : ISimpleFeature {
        Schemata.AddFeature<T>();
    }


    /// <summary>
    ///     Registers <typeparamref name="T" /> for keying without a schedule, so a job triggered
    ///     on-demand resolves its stable key after a restart.
    /// </summary>
    public SchedulingBuilder<TJob, TExecution> WithJob<T>()
        where T : class, IScheduledJob {
        Services.AddScheduledJob<T>();
        return this;
    }

    /// <summary>Registers <typeparamref name="T" /> against the supplied <see cref="IScheduleDefinition" />.</summary>
    public SchedulingBuilder<TJob, TExecution> WithJob<T>(IScheduleDefinition schedule)
        where T : class, IScheduledJob {
        Services.TryAddTransient<T>();
        Services.Configure<SchemataSchedulingOptions>(options => { options.Jobs.Add(new(typeof(T), schedule)); });
        return this;
    }

    /// <summary>Registers <typeparamref name="T" /> on a <see cref="CronSchedule" /> from the given expression.</summary>
    public SchedulingBuilder<TJob, TExecution> WithJob<T>(string cronExpression)
        where T : class, IScheduledJob {
        return WithJob<T>(new CronSchedule(cronExpression));
    }

    /// <summary>Registers <typeparamref name="T" /> for a one-time fire at <c>UtcNow + delay</c>.</summary>
    public SchedulingBuilder<TJob, TExecution> WithJob<T>(TimeSpan delay)
        where T : class, IScheduledJob {
        return WithJob<T>(new OneTimeSchedule(_time.GetUtcNow().UtcDateTime + delay));
    }

    /// <summary>Registers <typeparamref name="T" /> for a one-time fire at the given UTC <paramref name="runTime" />.</summary>
    public SchedulingBuilder<TJob, TExecution> WithJob<T>(DateTime runTime)
        where T : class, IScheduledJob {
        return WithJob<T>(new OneTimeSchedule(runTime));
    }
}
