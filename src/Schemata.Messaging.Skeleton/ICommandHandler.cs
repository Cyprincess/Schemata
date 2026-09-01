using Schemata.Abstractions;

namespace Schemata.Messaging.Skeleton;

/// <summary>Handles a <typeparamref name="TCommand" /> that produces no result.</summary>
/// <typeparam name="TCommand">The command type this handler answers.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Unit>
    where TCommand : ICommand;

/// <summary>Handles a <typeparamref name="TCommand" /> that produces a <typeparamref name="TResult" />.</summary>
/// <typeparam name="TCommand">The command type this handler answers.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>;
