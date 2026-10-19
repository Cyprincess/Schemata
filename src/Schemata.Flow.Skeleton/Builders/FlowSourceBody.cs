using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

internal static class FlowSourceBody
{
    internal static Func<FlowTaskContext, CancellationToken, ValueTask> Bind<TSource>(
        string source,
        Func<FlowTaskContext, TSource, CancellationToken, ValueTask> body
    ) where TSource : class, ICanonicalName {
        return async (ctx, ct) => {
            var entity = await ctx.SourceAsync<TSource>(source, ct);
            await body(ctx, entity, ct);
        };
    }
}
