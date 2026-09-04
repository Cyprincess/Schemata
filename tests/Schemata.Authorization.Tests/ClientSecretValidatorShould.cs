using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ClientSecretValidatorShould
{
    private static SchemataSecurity PasswordRow(string clientId) {
        return new() {
            Uid       = Guid.NewGuid(),
            Parent    = SecurityParents.Application(new() { ClientId = clientId }),
            Kind      = SecurityConstants.Kinds.Password,
            Usage     = SecurityConstants.Usages.Authentication,
            Algorithm = SecurityConstants.Algorithms.Pbkdf2,
            Status    = SecurityConstants.Statuses.Valid,
            Value     = "stored-hash",
        };
    }

    private static Mock<ISecurityStore<SchemataSecurity>> StoreWith(params SchemataSecurity[] rows) {
        var store = new Mock<ISecurityStore<SchemataSecurity>>();
        store
            .Setup(s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Enumerate(rows));
        return store;
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(params SchemataSecurity[] rows) {
        foreach (var row in rows) {
            yield return row;
        }
    }

    [Fact]
    public async Task Reject_A_Wrong_Secret_With_Invalid_Client() {
        var row        = PasswordRow("my-client");
        var securities = StoreWith(row);
        var verifier   = new Mock<ISecretVerifier>();
        verifier.Setup(v => v.VerifyAsync(row, "wrong", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => ClientSecretValidator.ValidateAsync(
                securities.Object, verifier.Object,
                new() { ClientId = "my-client", ClientType = ClientTypes.Confidential }, "wrong", default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }

    [Fact]
    public async Task Query_The_Newest_Valid_Authentication_Password_Row() {
        var securities = StoreWith(PasswordRow("my-client"));
        var verifier   = new Mock<ISecretVerifier>();
        verifier.Setup(v => v.VerifyAsync(It.IsAny<SchemataSecurity>(), "my-secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await ClientSecretValidator.ValidateAsync(
            securities.Object, verifier.Object,
            new() { ClientId = "my-client", ClientType = ClientTypes.Confidential }, "my-secret", default);

        securities.Verify(
            s => s.ListByParentAsync(
                "applications/my-client",
                SecurityConstants.Kinds.Password,
                SecurityConstants.Usages.Authentication,
                SecurityConstants.Statuses.Valid,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reject_When_No_Valid_Password_Row_Exists() {
        var securities = StoreWith();
        var verifier   = new Mock<ISecretVerifier>();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => ClientSecretValidator.ValidateAsync(
                securities.Object, verifier.Object,
                new() { ClientId = "my-client", ClientType = ClientTypes.Confidential }, "my-secret", default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
    }

    [Fact]
    public async Task Reject_A_Missing_Secret_For_Confidential_Clients() {
        var securities = new Mock<ISecurityStore<SchemataSecurity>>();

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => ClientSecretValidator.ValidateAsync(
                securities.Object, new Mock<ISecretVerifier>().Object,
                new() { ClientType = ClientTypes.Confidential }, null, default));

        Assert.Equal(OAuthErrors.InvalidClient, ex.Status);
        securities.Verify(
            s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Accept_A_Public_Client_Without_Secret() {
        var securities = new Mock<ISecurityStore<SchemataSecurity>>();

        await ClientSecretValidator.ValidateAsync(
            securities.Object, new Mock<ISecretVerifier>().Object,
            new() { ClientType = ClientTypes.Public }, null, default);

        securities.Verify(
            s => s.ListByParentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
