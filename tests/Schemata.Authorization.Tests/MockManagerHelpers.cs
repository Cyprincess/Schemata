using System.Threading;
using Moq;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Wires a mocked <see cref="IApplicationManager{TApplication}" /> to answer the typed
///     metadata checks (grant types, response types, scopes) through
///     <see cref="SchemataApplicationMetadata" />, the same predicates the production manager
///     delegates to, so tests only arrange the entity.
/// </summary>
public static class MockManagerHelpers
{
    public static void SetupTypedMetadata(this Mock<IApplicationManager<SchemataApplication>> manager) {
        manager.Setup(m => m.HasGrantTypeAsync(It.IsAny<SchemataApplication?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataApplication? a, string? g, CancellationToken _) => g is not null && a is not null && SchemataApplicationMetadata.HasGrantType(a, g));

        manager.Setup(m => m.HasResponseTypeAsync(It.IsAny<SchemataApplication?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataApplication? a, string? r, CancellationToken _) => r is not null && a is not null && SchemataApplicationMetadata.HasResponseType(a, r));

        manager.Setup(m => m.HasScopeAsync(It.IsAny<SchemataApplication?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((SchemataApplication? a, string? s, CancellationToken _) => s is not null && a is not null && SchemataApplicationMetadata.HasScope(a, s));
    }
}
