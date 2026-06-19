using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

/// <summary>
///     Backfills <see cref="SchemataJob.JobKey" /> from the assembly-qualified job type
///     column. Reflection reads the hidden column, resolves the CLR type through
///     <see cref="Type.GetType(string, bool)" />, and writes the registry-resolved stable key.
/// </summary>
public static class AqnJobKeyMigration
{
    private static readonly PropertyInfo JobTypeProperty = typeof(SchemataJob).GetProperty("JobType")!;

    /// <summary>Updates persisted job rows that can be resolved through the scheduled job registry.</summary>
    public static async Task RunAsync(
        IRepository<SchemataJob> jobs,
        IScheduledJobRegistry    registry,
        CancellationToken        ct,
        ILogger?                 logger = null
    ) {
        var migrated = new List<SchemataJob>();
        await foreach (var job in jobs.ListAsync(q => q.Where(j => j.JobKey == null), ct)) {
            var aqn = (string?)JobTypeProperty.GetValue(job);
            if (string.IsNullOrWhiteSpace(aqn)) {
                continue;
            }

            var type = Type.GetType(aqn, false);
            if (type is null) {
                logger?.LogWarning(
                    "Could not resolve CLR type for legacy JobType '{Aqn}' on job '{JobName}'; leaving JobKey unset.",
                    aqn, job.Name);
                continue;
            }

            var key = registry.ResolveKey(type);
            if (string.IsNullOrWhiteSpace(key)) {
                logger?.LogWarning(
                    "Type '{Type}' for legacy JobType '{Aqn}' on job '{JobName}' is not registered in IScheduledJobRegistry; leaving JobKey unset.",
                    type, aqn, job.Name);
                continue;
            }

            job.JobKey = key;
            migrated.Add(job);
        }

        foreach (var job in migrated) {
            await jobs.UpdateAsync(job, ct);
        }

        if (migrated.Count > 0) {
            await jobs.CommitAsync(ct);
        }
    }
}
