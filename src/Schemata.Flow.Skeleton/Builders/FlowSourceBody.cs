using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

internal static class FlowSourceBody
{
    internal static Func<FlowTaskContext, ValueTask> Bind<TSource>(
        string                                    source,
        Func<FlowTaskContext, TSource, ValueTask> body
    ) where TSource : class, ICanonicalName {
        return async ctx => {
            var entity = await ctx.SourceAsync<TSource>(source);
            await body(ctx, entity);
        };
    }
}
