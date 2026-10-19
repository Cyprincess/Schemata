using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Messaging.Skeleton.Runtime;

public sealed class InProcessStreamDispatcher(IServiceProvider services, IMessageExecutionScopeFactory scopes) : IStreamDispatcher
{
    public IAsyncEnumerable<TItem> Stream<TRequest, TItem>(TRequest request, ClaimsPrincipal? principal = null, CancellationToken ct = default)
        where TRequest : IStreamRequest<TItem> {
        var captured = MessageContexts.Capture(services);
        var context = new MessageContext(captured.Items.ToFrozenDictionary(StringComparer.Ordinal));
        return new StreamSequence<TRequest, TItem>(scopes, request, context, Clone(principal), ct);
    }

    private static ClaimsPrincipal? Clone(ClaimsPrincipal? principal) => principal is null ? null
        : new ClaimsPrincipal(principal.Identities.Select(identity => identity.Clone()));

    private sealed class StreamSequence<TRequest, TItem>(IMessageExecutionScopeFactory scopes, TRequest request,
        MessageContext captured, ClaimsPrincipal? principal, CancellationToken invocation) : IAsyncEnumerable<TItem>
        where TRequest : IStreamRequest<TItem>
    {
        public IAsyncEnumerator<TItem> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator<TRequest, TItem>(scopes, request, captured, Clone(principal), invocation, cancellationToken);
    }

    private sealed class Enumerator<TRequest, TItem> : IAsyncEnumerator<TItem> where TRequest : IStreamRequest<TItem>
    {
        private readonly IMessageExecutionScopeFactory _scopes;
        private readonly TRequest _request;
        private readonly MessageContext _captured;
        private readonly ClaimsPrincipal? _principal;
        private readonly CancellationTokenSource? _linked;
        private readonly CancellationToken _ct;
        private MessageExecutionScope? _scope;
        private AdviceContext? _advice;
        private IAsyncEnumerator<TItem>? _inner;
        private bool _finished;
        private TItem _current = default!;

        internal Enumerator(IMessageExecutionScopeFactory scopes, TRequest request, MessageContext captured,
            ClaimsPrincipal? principal, CancellationToken invocation, CancellationToken enumeration) {
            _scopes = scopes; _request = request; _captured = captured; _principal = principal;
            if (invocation.CanBeCanceled && enumeration.CanBeCanceled && invocation != enumeration) {
                _linked = CancellationTokenSource.CreateLinkedTokenSource(invocation, enumeration);
                _ct = _linked.Token;
            } else _ct = enumeration.CanBeCanceled ? enumeration : invocation;
        }

        public TItem Current => _current;

        public async ValueTask<bool> MoveNextAsync() {
            if (_finished) return false;
            IDisposable? execution = null;
            Exception? failure = null;
            try {
                _scope ??= await _scopes.CreateAsync(_captured, _ct);
                execution = _scope.Enter();
                _ct.ThrowIfCancellationRequested();
                _advice ??= new AdviceContext(_scope.Services);
                if (_inner is null) {
                    await _scope.RestoreAsync(_captured, _ct);
                    var context = new StreamExecutionContext(_advice, _principal);
                    var advisors = _scope.Services.GetServices<IStreamPipelineAdvisor<TRequest, TItem>>().OrderBy(value => value.Order).ToList();
                    var validation = _scope.Services.GetKeyedService<IStreamPipelineAdvisor<TRequest, TItem>>(RequestPipelineStages.Validation);
                    if (validation is not null) {
                        var index = advisors.FindIndex(value => value.Order > validation.Order);
                        if (index < 0) advisors.Add(validation); else advisors.Insert(index, validation);
                    }
                    StreamContinuation<TItem> next = token => {
                        var handlers = _scope.Services.GetServices<IStreamRequestHandler<TRequest, TItem>>().ToArray();
                        if (handlers.Length != 1) throw new InvalidOperationException($"Expected exactly one stream handler for '{typeof(TRequest).FullName}', found {handlers.Length}.");
                        return handlers[0].HandleAsync(_request, context, token);
                    };
                    for (var index = advisors.Count - 1; index >= 0; index--) {
                        var downstream = next;
                        var advisor = advisors[index];
                        next = token => advisor.AdviseAsync(context, _request, downstream, token);
                    }
                    _inner = next(_ct).GetAsyncEnumerator(_ct);
                }
                if (await _inner.MoveNextAsync()) {
                    _current = _inner.Current;
                    return true;
                }
                await FinishAsync();
                return false;
            } catch (Exception error) {
                failure = error;
                try { await FinishAsync(); }
                catch (Exception cleanup) { failure = new AggregateException(error, cleanup); }
                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            } finally {
                try { execution?.Dispose(); }
                catch (Exception cleanup) {
                    ExceptionDispatchInfo.Capture(failure is null ? cleanup : new AggregateException(failure, cleanup)).Throw();
                }
            }
        }

        public async ValueTask DisposeAsync() {
            if (_finished) return;
            if (_scope is null) { _finished = true; _linked?.Dispose(); return; }
            IDisposable? execution = null;
            Exception? failure = null;
            try {
                execution = _scope.Enter();
                _advice ??= new AdviceContext(_scope.Services);
                await FinishAsync();
            } catch (Exception error) {
                failure = error;
                throw;
            } finally {
                Exception? cleanup = null;
                try { await FinishAsync(); }
                catch (Exception error) { cleanup = error; }
                try { execution?.Dispose(); }
                catch (Exception error) { cleanup = cleanup is null ? error : new AggregateException(cleanup, error); }
                if (cleanup is not null) {
                    ExceptionDispatchInfo.Capture(failure is null ? cleanup : new AggregateException(failure, cleanup)).Throw();
                }
            }
        }

        private async ValueTask FinishAsync() {
            if (_finished) return;
            _finished = true;
            var inner = _inner;
            _inner = null;
            var scope = _scope;
            _scope = null;
            Exception? failure = null;
            try { if (inner is not null) await inner.DisposeAsync(); }
            catch (Exception error) { failure = error; }
            try { if (scope is not null) await scope.DisposeAsync(); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            finally { _linked?.Dispose(); }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
