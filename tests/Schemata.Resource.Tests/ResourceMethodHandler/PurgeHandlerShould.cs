using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Expressions.Aip;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class PurgeHandlerShould
{
    [Fact]
    public async Task Invoke_PersistsRequestData_NotClosure() {
        var dispatcher = new CapturingOperationDispatcher();
        var services   = Services(dispatcher);
        var handler    = new PurgeHandler<TrashStudent>(services);

        var operation = await handler.InvokeAsync(null, new() { Filter = "*", Force = true }, null,
                                                  Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        Assert.Equal("operations/test-operation", operation.CanonicalName);
        Assert.Equal($"{Verbs.Purge}:{ResourceNameDescriptor.ForType<TrashStudent>().Collection}", dispatcher.Key);
        var args = Assert.IsType<PurgeOperationArgs>(dispatcher.Args);
        Assert.Equal("*", args.Filter);
        Assert.True(args.Force);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invoke_EmptyFilter_ThrowsValidationException(string? filter) {
        var services = Services(new CapturingOperationDispatcher());
        var handler  = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.InvokeAsync(
                                                                                 null, new() { Filter = filter }, null,
                                                                                 null,
                                                                                 CancellationToken.None)
                                                                            .AsTask());

        AssertInvalidFilter(ex);
    }

    [Fact]
    public async Task Invoke_InvalidFilter_ThrowsValidationException() {
        var services = Services(new CapturingOperationDispatcher());
        var handler  = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.InvokeAsync(
                                                                                 null, new() { Filter = "(" }, null,
                                                                                 null,
                                                                                 CancellationToken.None)
                                                                            .AsTask());

        AssertInvalidFilter(ex);
    }

    [Fact]
    public async Task Invoke_MissingDispatcher_ThrowsBridgeMessage() {
        var services = new ServiceCollection().AddAipExpressions().BuildServiceProvider();
        var handler  = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.InvokeAsync(
                                                                                       null, new() { Filter = "*" },
                                                                                       null, null,
                                                                                       CancellationToken.None)
                                                                                  .AsTask());

        Assert.Contains("IOperationDispatcher", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Schemata.Scheduling.Http/Grpc", ex.Message, StringComparison.Ordinal);
    }

    private static IServiceProvider Services(IOperationDispatcher dispatcher) {
        return new ServiceCollection().AddAipExpressions().AddSingleton(dispatcher).BuildServiceProvider();
    }

    private static void AssertInvalidFilter(ValidationException ex) {
        var detail = Assert.IsType<BadRequestDetail>(Assert.Single(ex.Details!));
        Assert.NotNull(detail.FieldViolations);
        Assert.Contains(detail.FieldViolations, e => e.Reason == FieldReasons.InvalidFilter);
    }

    #region Nested type: CapturingOperationDispatcher

    private sealed class CapturingOperationDispatcher : IOperationDispatcher
    {
        public string? Key { get; private set; }

        public object? Args { get; private set; }

        #region IOperationDispatcher Members

        public Task<Operation> DispatchAsync<TArgs>(string operationKey, TArgs args, CancellationToken ct)
            where TArgs : class {
            Key  = operationKey;
            Args = args;
            return Task.FromResult(new Operation {
                Name = "test-operation", CanonicalName = "operations/test-operation",
            });
        }

        #endregion
    }

    #endregion
}
