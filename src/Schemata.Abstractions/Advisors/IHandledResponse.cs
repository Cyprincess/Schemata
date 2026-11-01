namespace Schemata.Abstractions.Advisors;

/// <summary>
///     The handled-output contract shared by every advisor stage that can short-circuit a
///     pipeline. An advisor returning <see cref="AdviseResult.Handle" /> writes its response
///     through <see cref="SetResponse" />, and the pipeline owner reads it back through
///     <see cref="Response" /> from the same instance.
/// </summary>
/// <typeparam name="TResponse">The response type the stage produces when handled.</typeparam>
public interface IHandledResponse<TResponse>
{
    /// <summary>The response written by the handling advisor, or <see langword="null" /> when none was written.</summary>
    TResponse? Response { get; }

    /// <summary>Whether a handling advisor wrote a response.</summary>
    bool Handled { get; }

    /// <summary>Writes the response the pipeline owner reads back.</summary>
    /// <param name="response">The handled response.</param>
    void SetResponse(TResponse response);
}
