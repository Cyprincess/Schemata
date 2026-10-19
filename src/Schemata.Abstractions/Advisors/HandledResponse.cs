namespace Schemata.Abstractions.Advisors;

/// <summary>
///     Explicit output carrier for an advisor stage that can short-circuit a pipeline. The stage
///     invocation creates one instance per dispatch and passes it to every advisor; an advisor
///     returning <see cref="AdviseResult.Handle" /> writes the response to
///     <see cref="Response" />, and the pipeline owner reads it back from the same instance.
/// </summary>
/// <typeparam name="TResponse">The response type the stage produces when handled.</typeparam>
public sealed class HandledResponse<TResponse> : IHandledResponse<TResponse> where TResponse : class
{
    /// <summary>The response written by the handling advisor.</summary>
    public TResponse? Response { get; set; }

    /// <inheritdoc />
    public bool Handled => Response is not null;

    /// <inheritdoc />
    public void SetResponse(TResponse response) { Response = response; }
}
