using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Tests.Fixtures;

/// <summary>
///     A configurable wrap advisor over <see cref="RenameWidget" />. It records the ambient context
///     it observed and appends before and after markers to a shared trail. When
///     <paramref name="callNext" /> is <see langword="false" /> it returns its own short-circuit
///     value without invoking the continuation.
/// </summary>
public sealed class OrderedRenameAdvisor(int order, string tag, List<string> trail, bool callNext)
    : IRequestPipelineAdvisor<RenameWidget, string>
{
    public int Order => order;

    public AdviceContext? ObservedContext { get; private set; }

    public async Task<string> AdviseAsync(
        AdviceContext                      ctx,
        RenameWidget                       request,
        RequestHandlerContinuation<string> next,
        CancellationToken                  ct = default) {
        ObservedContext = ctx;
        trail.Add($"{tag}:before");
        if (!callNext) {
            return $"{tag}:short";
        }

        var response = await next(ct);
        trail.Add($"{tag}:after");
        return response;
    }
}