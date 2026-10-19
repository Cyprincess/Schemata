using System;

namespace Schemata.Abstractions.Advisors;

/// <summary>
///     The explicit execution context handed to one advisor pipeline: the service provider scoped
///     to the pipeline's owning unit of work. Pipeline roots construct it, every advisor and handler
///     receives it as a parameter, and no state is shared between contexts.
/// </summary>
public class AdviceContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceContext" /> class.
    /// </summary>
    /// <param name="sp">The <see cref="IServiceProvider" /> available to advisors during pipeline execution.</param>
    public AdviceContext(IServiceProvider sp) { ServiceProvider = sp; }

    /// <summary>
    ///     The <see cref="IServiceProvider" /> for resolving services within advisors.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }
}
