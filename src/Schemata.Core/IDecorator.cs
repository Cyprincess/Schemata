namespace Schemata.Core;

/// <summary>
///     Extends a service the framework registers for itself. A feature registers a decorator for
///     <typeparamref name="T" />, and the framework composes every registered decorator, in
///     registration order, over its own implementation; the last decorator becomes the public
///     service.
/// </summary>
/// <typeparam name="T">The framework service type being decorated.</typeparam>
public interface IDecorator<T>
    where T : class
{
    /// <summary>Wraps <paramref name="inner" /> and returns the service the framework publishes.</summary>
    /// <param name="inner">The implementation composed so far.</param>
    /// <returns>The composed implementation.</returns>
    T Decorate(T inner);
}
