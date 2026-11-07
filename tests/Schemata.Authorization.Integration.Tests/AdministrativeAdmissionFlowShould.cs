using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public class AdministrativeAdmissionFlowShould
{
    [Theory]
    [InlineData(GrantTypes.AuthorizationCode, false)]
    [InlineData(GrantTypes.RefreshToken, false)]
    [InlineData(GrantTypes.TokenExchange, false)]
    [InlineData(GrantTypes.RefreshToken, true)]
    public async Task Inactive_Subject_Publishes_No_Credentials(string grantType, bool rotation) {
        var subjects = new InactiveSubjectProvider();
        using var factory = new WebAppFactory().WithServices(services => services.AddSingleton<ISubjectProvider>(subjects));
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuthorizationSignInService>();
        var grant = new AuthorizationGrantContext {
            Subject = "users/inactive",
            SubjectKind = GrantSubjectKinds.EndUser,
            Profile = GrantProfiles.OpenIdConnect,
            Source = GrantTypes.AuthorizationCode,
            Scope = $"{Scopes.OpenId} {Scopes.DeviceSso} {Scopes.OfflineAccess}",
            SessionId = "sid-1",
            NativeSessionKind = NativeSessionKinds.Offline,
            Family = "family",
            FamilyEstablished = true,
        };
        var properties = new Dictionary<string, string?> {
            [Properties.GrantType] = grantType,
            [Properties.Scope] = grant.Scope,
            [Properties.SessionId] = "sid-1",
            [Properties.GrantContext] = JsonSerializer.Serialize(grant, Schemata.Common.SchemataJson.Default),
        };
        if (rotation) {
            properties[Properties.RefreshPredecessor] = JsonSerializer.Serialize(new SchemataToken {
                Family = "family", ReferenceId = "predecessor",
            }, Schemata.Common.SchemataJson.Default);
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, "users/inactive"),
        ], "grant"));

        var error = await Assert.ThrowsAsync<OAuthException>(() => service.IssueAsync(
            principal, properties, AuthorizationSignInResponseKind.Token));

        Assert.Equal(OAuthErrors.InvalidGrant, error.Status);
        Assert.Equal(1, subjects.Validations);
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
        Assert.Equal(0, await repository.CountAsync(query => query));
    }

    private sealed class InactiveSubjectProvider : ISubjectProvider
    {
        public int Validations { get; private set; }

        public Task<IEnumerable<Claim>> GetClaimsAsync(string subject, CancellationToken ct = default) {
            return Task.FromResult(Enumerable.Empty<Claim>());
        }

        public Task<bool> ValidateAsync(string subject, CancellationToken ct = default) {
            Validations++;
            return Task.FromResult(false);
        }
    }
}
