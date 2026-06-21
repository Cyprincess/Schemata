using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class InsightSecurityGateShould
{
    [Fact]
    public async Task Throws_When_Source_Access_Is_Denied() {
        var access = new Mock<IAccessProvider<Row, QueryInsightRequest>>();
        access.Setup(a => a.HasAccessAsync(It.IsAny<Row?>(),
                                           It.IsAny<AccessContext<QueryInsightRequest>>(),
                                           It.IsAny<ClaimsPrincipal?>(),
                                           It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var services = Provider(access.Object, null);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => InsightSecurityGate.AuthorizeAsync(typeof(Row), new(), null, services, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_The_Entitlement_When_Allowed() {
        var access = new Mock<IAccessProvider<Row, QueryInsightRequest>>();
        access.Setup(a => a.HasAccessAsync(It.IsAny<Row?>(),
                                           It.IsAny<AccessContext<QueryInsightRequest>>(),
                                           It.IsAny<ClaimsPrincipal?>(),
                                           It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        Expression<Func<Row, bool>> predicate = row => row.Age > 0;
        var entitlement = new Mock<IEntitlementProvider<Row, QueryInsightRequest>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                                  It.IsAny<AccessContext<QueryInsightRequest>>(),
                                  It.IsAny<ClaimsPrincipal?>(),
                                  It.IsAny<CancellationToken>()))
                   .ReturnsAsync(predicate);

        var services = Provider(access.Object, entitlement.Object);

        var result = await InsightSecurityGate.AuthorizeAsync(typeof(Row), new(), null, services, CancellationToken.None);

        Assert.Same(predicate, result);
    }

    [Fact]
    public async Task Returns_Null_When_No_Providers_Registered() {
        var services = Provider(null, null);

        var result = await InsightSecurityGate.AuthorizeAsync(typeof(Row), new(), null, services, CancellationToken.None);

        Assert.Null(result);
    }

    private static IServiceProvider Provider(
        IAccessProvider<Row, QueryInsightRequest>?      access,
        IEntitlementProvider<Row, QueryInsightRequest>? entitlement
    ) {
        var services = new ServiceCollection();
        if (access is not null) {
            services.AddSingleton(access);
        }

        if (entitlement is not null) {
            services.AddSingleton(entitlement);
        }

        return services.BuildServiceProvider();
    }

    #region Nested type: Row

    // Public so Moq's dynamic proxy can implement IAccessProvider<Row, …> over it.
    public sealed class Row
    {
        public int Age { get; set; }
    }

    #endregion
}
