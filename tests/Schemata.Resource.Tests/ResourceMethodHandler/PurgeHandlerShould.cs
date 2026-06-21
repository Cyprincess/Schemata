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
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invoke_EmptyFilter_ThrowsValidationException(string? filter) {
        var services = Services();
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
        var services = Services();
        var handler  = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => handler.InvokeAsync(
                                                                                 null, new() { Filter = "(" }, null,
                                                                                 null,
                                                                                 CancellationToken.None)
                                                                            .AsTask());

        AssertInvalidFilter(ex);
    }

    [Fact]
    public async Task Invoke_MissingScheduler_ThrowsInvalidOperation() {
        var services = Services();
        var handler  = new PurgeHandler<TrashStudent>(services);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.InvokeAsync(
                                                                                null, new() { Filter = "*" },
                                                                                null, null,
                                                                                CancellationToken.None)
                                                                           .AsTask());

        Assert.Contains("scheduler", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IServiceProvider Services() {
        return new ServiceCollection().AddAipExpressions().BuildServiceProvider();
    }

    private static void AssertInvalidFilter(ValidationException ex) {
        var detail = Assert.IsType<BadRequestDetail>(Assert.Single(ex.Details!));
        Assert.NotNull(detail.FieldViolations);
        Assert.Contains(detail.FieldViolations, e => e.Reason == FieldReasons.InvalidFilter);
    }
}
