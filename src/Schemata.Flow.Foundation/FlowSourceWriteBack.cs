using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

internal sealed class FlowSourceWriteBack(FlowExecutionContext execution)
{
    public void Touch<TSource>(TSource source)
        where TSource : class, ICanonicalName {
        if (!string.IsNullOrEmpty(source.CanonicalName)) {
            execution.TouchedSources[(typeof(TSource), source.CanonicalName)] = source;
        }
    }
}
