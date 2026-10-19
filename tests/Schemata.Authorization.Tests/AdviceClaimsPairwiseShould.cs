using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Entity.Repository;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceClaimsPairwiseShould
{
    [Fact]
    public async Task Preserve_The_Subject_Claim_Metadata_When_Projecting_The_Pairwise_Value() {
        var app = new SchemataApplication {
            ClientId      = "client-1",
            CanonicalName = "applications/client-1",
            SubjectType   = SubjectTypes.Pairwise,
        };
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(m => m.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        // An existing mapping row makes EnsureMappingAsync return the stored pairwise value,
        // so the advisor runs its replacement loop without touching the mapping owner.
        var mappings = new Mock<IRepository<SchemataSubjectMapping>>();
        mappings.Setup(r => r.FirstOrDefaultAsync(
                    It.IsAny<Func<IQueryable<SchemataSubjectMapping>, IQueryable<SchemataSubjectMapping>>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .ReturnsAsync(new SchemataSubjectMapping {
                    Application     = "applications/client-1",
                    Subject         = "users/alice",
                    PairwiseSubject = "pairwise-1",
                });
        var translator = new PairwiseSubjectTranslator<SchemataApplication, SchemataSubjectMapping>(
            apps.Object,
            new Mock<ISubjectIdentifierService>().Object,
            mappings.Object,
            new Mock<IResourceMutation<SchemataSubjectMapping>>().Object,
            new ServiceCollection().BuildServiceProvider());
        var advisor = new AdviceClaimsPairwise<SchemataApplication, SchemataSubjectMapping>(apps.Object, translator);

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, "users/alice", "json", "https://issuer.example", "https://original.example") {
                Properties = { [ClaimDestinations.IdentityToken] = "sentinel" },
            },
            new(Claims.ClientId, "client-1"),
        };

        await advisor.AdviseAsync(new(new ServiceCollection().BuildServiceProvider()), claims, new AuthorizationClaimContext());

        var replaced = Assert.Single(claims, c => c.Type == IdentityClaims.Subject);
        Assert.Equal("pairwise-1", replaced.Value);
        Assert.Equal("json", replaced.ValueType);
        Assert.Equal("https://issuer.example", replaced.Issuer);
        Assert.Equal("https://original.example", replaced.OriginalIssuer);
        Assert.Single(replaced.Properties);
        Assert.Equal("sentinel", replaced.Properties[ClaimDestinations.IdentityToken]);
    }

    [Fact]
    public async Task Project_Forward_And_Reverse_Under_A_Host_Default_Pairwise_Subject_Type() {
        var app = new SchemataApplication {
            ClientId            = "client-1",
            CanonicalName       = "applications/client-1",
            SectorIdentifierUri = "https://rp.example",
        };
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(m => m.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>())).ReturnsAsync(app);
        apps.Setup(m => m.EnlistPublicationAsync(
                   It.IsAny<Schemata.Entity.Repository.IUnitOfWork>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);

        var rows = new List<SchemataSubjectMapping>();
        var mappings = new Mock<IRepository<SchemataSubjectMapping>>();
        mappings.Setup(r => r.FirstOrDefaultAsync(
                    It.IsAny<Func<IQueryable<SchemataSubjectMapping>, IQueryable<SchemataSubjectMapping>>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .Returns((Func<IQueryable<SchemataSubjectMapping>, IQueryable<SchemataSubjectMapping>> query,
                          CancellationToken _, QueryOptions _) => new ValueTask<SchemataSubjectMapping?>(
                                              query(rows.AsQueryable()).FirstOrDefault()));
        var transaction = new Mock<Schemata.Entity.Repository.IUnitOfWork>();
        transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mappings.Setup(r => r.Begin()).Returns(transaction.Object);
        var mutation = new Mock<IResourceMutation<SchemataSubjectMapping>>();
        mutation.Setup(m => m.CreateAsync(
                    It.IsAny<SchemataSubjectMapping>(),
                    It.IsAny<Schemata.Entity.Repository.IUnitOfWork>(),
                    It.IsAny<CancellationToken>()))
                .Callback((SchemataSubjectMapping row, Schemata.Entity.Repository.IUnitOfWork _, CancellationToken _) => rows.Add(row))
                .ReturnsAsync(MutationResult.Applied);
        using var provider = new ServiceCollection().AddSingleton(mappings.Object).BuildServiceProvider();
        var subjects = new SubjectIdentifierService(
            Options.Create(new SchemataAuthorizationOptions {
                SubjectType  = SubjectTypes.Pairwise,
                PairwiseSalt = "test-salt",
            }));
        var translator = new PairwiseSubjectTranslator<SchemataApplication, SchemataSubjectMapping>(
            apps.Object, subjects, mappings.Object, mutation.Object, provider);

        // The application carries no subject_type override: the host default decides.
        var caller   = new ClaimsPrincipal(new ClaimsIdentity([new(Claims.ClientId, "client-1")], "grant"));
        var forward  = await translator.ToPairwiseAsync("users/alice", caller);
        var backward = await translator.ToCanonicalAsync(forward, caller);

        Assert.NotEqual("users/alice", forward);
        Assert.Equal(forward, Assert.Single(rows).PairwiseSubject);
        Assert.Equal("users/alice", backward);
    }

    [Fact]
    public async Task Resolve_A_Stored_Mapping_For_An_Application_Whose_Sector_Metadata_Was_Cleared() {
        var app = new SchemataApplication {
            ClientId      = "client-1",
            CanonicalName = "applications/client-1",
            SubjectType   = SubjectTypes.Pairwise,
            // sector_identifier_uri and redirect_uris were cleared by a later replace; the
            // stored projection must still resolve from the row alone.
        };
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(m => m.FindByClientIdAsync("client-1", It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var rows = new List<SchemataSubjectMapping> {
            new() {
                Application     = "applications/client-1",
                Subject         = "users/alice",
                PairwiseSubject = "pairwise-stored",
            },
        };
        var mappings = new Mock<IRepository<SchemataSubjectMapping>>();
        mappings.Setup(r => r.FirstOrDefaultAsync(
                    It.IsAny<Func<IQueryable<SchemataSubjectMapping>, IQueryable<SchemataSubjectMapping>>>(),
                    It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .Returns((Func<IQueryable<SchemataSubjectMapping>, IQueryable<SchemataSubjectMapping>> query,
                          CancellationToken _, QueryOptions _) => new ValueTask<SchemataSubjectMapping?>(
                                              query(rows.AsQueryable()).FirstOrDefault()));
        var translator = new PairwiseSubjectTranslator<SchemataApplication, SchemataSubjectMapping>(
            apps.Object,
            new SubjectIdentifierService(Options.Create(new SchemataAuthorizationOptions())),
            mappings.Object,
            new Mock<IResourceMutation<SchemataSubjectMapping>>().Object,
            new ServiceCollection().BuildServiceProvider());

        var caller   = new ClaimsPrincipal(new ClaimsIdentity([new(Claims.ClientId, "client-1")], "grant"));
        var forward  = await translator.ToPairwiseAsync("users/alice", caller);
        var backward = await translator.ToCanonicalAsync("pairwise-stored", caller);

        // A stored row resolves without consulting the sector-dependent owner probe.
        Assert.Equal("pairwise-stored", forward);
        Assert.Equal("users/alice", backward);
    }
}
