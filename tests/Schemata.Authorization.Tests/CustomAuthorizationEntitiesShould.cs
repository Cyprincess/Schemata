using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Mutations;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Caching.Skeleton;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Entity.Repository;
using Schemata.Security.Foundation;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class CustomAuthorizationEntitiesShould
{
    [Fact]
    public void Close_The_Graph_On_The_Host_Entities_And_The_Security_Token() {
        var builder = WebApplication.CreateBuilder();
        SchemataOptions? options = null;
        builder.UseSchemata(schema => {
            options = schema.Options;
            schema.UseSecurity<MySecurity, MyToken>()
                  .UseAuthorization<MyApplication, MyGrant, MyScope, MyMapping>(o => o.Issuer = "https://as.example")
                  .UsePairwiseSubjects()
                  .MapHttp();
        });

        var services = builder.Services;
        Assert.Contains(services, d => d.ServiceType == typeof(IResourceMutation<MyApplication>)
                                    && d.ImplementationType == typeof(ApplicationResourceMutation<MyApplication, MyGrant, MyMapping, MySecurity, MyToken>));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IResourceMutation<SchemataApplication>));
        Assert.Contains(services, d => d.ServiceType == typeof(ITokenStore<MyToken>));
        Assert.Contains(services, d => d.ServiceType == typeof(RepositoryTokenStore<MyToken>));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ITokenStore<SchemataToken>));
        Assert.Contains(services, d => d.ServiceType == typeof(TokenService<MySecurity>));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(TokenService<SchemataSecurity>));
        Assert.Contains(services, d => d.ServiceType == typeof(JwksHandler<MySecurity>));
        Assert.Contains(services, d => d.ServiceType == typeof(IClientAuthentication<MyApplication>)
                                    && d.ImplementationType == typeof(ClientSecretBasicAuthentication<MyApplication, MySecurity>));
        Assert.Contains(services, d => d.ServiceType == typeof(PairwiseSubjectTranslator<MyApplication, MyMapping>));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(PairwiseSubjectTranslator<SchemataApplication, SchemataSubjectMapping>));
        Assert.NotNull(options);
        Assert.True(options.HasFeature(typeof(SchemataAuthorizationFeature<MyApplication, MyGrant, MyScope, MyMapping, MySecurity, MyToken>)));
        Assert.False(options.HasFeature(
            typeof(SchemataAuthorizationFeature<SchemataApplication, SchemataAuthorization, SchemataScope, SchemataSubjectMapping, SchemataSecurity, SchemataToken>)));

        using var app = builder.Build();
        var registry = app.Services.GetRequiredService<ResourceRegistry>();
        Assert.NotNull(registry.GetResource(typeof(MyApplication)));
        Assert.NotNull(registry.GetResource(typeof(MyScope)));
        Assert.NotNull(registry.GetResource(typeof(MyToken)));
        Assert.Null(registry.GetResource(typeof(SchemataApplication)));
        Assert.Null(registry.GetResource(typeof(SchemataScope)));
        Assert.Null(registry.GetResource(typeof(SchemataToken)));
    }

    [Fact]
    public async Task Publish_Jwks_From_The_Host_Security_Entity() {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var row = new MySecurity {
            Parent     = "https://as.example",
            Name       = "my-security",
            Kind       = SecurityConstants.Kinds.PrivateKey,
            Usage      = SecurityConstants.Usages.Signing,
            Algorithm  = SecurityConstants.Algorithms.Rsa,
            Kid        = "my-key-1",
            Value      = rsa.ExportPkcs8PrivateKeyPem(),
            Status     = SecurityConstants.Statuses.Valid,
            CreateTime = DateTime.UtcNow,
        };
        var store = new Mock<ISecurityStore<MySecurity>>();
        store.Setup(s => s.ListByParentAsync(
                       It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                       It.IsAny<CancellationToken>()))
             .Returns(Rows(row));
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new System.Net.Http.HttpClient());
        var handler = new JwksHandler<MySecurity>(
            store.Object,
            http.Object,
            Mock.Of<ICacheProvider>(),
            Options.Create(new SchemataSecurityOptions()),
            Options.Create(new SchemataAuthorizationOptions { Issuer = "https://as.example" }));

        var result = await handler.ExecuteAsync();

        using var document = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(result.Data));
        var key = Assert.Single(document.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal("my-key-1", key.GetProperty("kid").GetString());
    }

    [Fact]
    public void Reject_Authorization_Without_A_Registered_Security_Feature() {
        var builder = WebApplication.CreateBuilder();

        var error = Assert.Throws<InvalidOperationException>(() => builder.UseSchemata(
            schema => schema.UseAuthorization<MyApplication, MyGrant, MyScope, MyMapping>(o => o.Issuer = "https://as.example")));

        Assert.Contains("UseSecurity", error.Message);
    }

    [Fact]
    public void Reject_Ambiguous_Security_Features() {
        var builder = WebApplication.CreateBuilder();

        var error = Assert.Throws<InvalidOperationException>(() => builder.UseSchemata(
            schema => schema.UseSecurity()
                            .UseSecurity<MySecurity, MyToken>()
                            .UseAuthorization<MyApplication, MyGrant, MyScope, MyMapping>(o => o.Issuer = "https://as.example")));

        Assert.Contains("exactly one", error.Message);
    }

    [Fact]
    public async Task Persist_Pairwise_Subjects_Through_The_Host_Mapping_Entity() {
        var application = new MyApplication { ClientId = "client-1", CanonicalName = "applications/client-1" };
        var rows        = new List<MyMapping>();
        var mappings    = Repository<MyMapping>();
        mappings.Setup(r => r.FirstOrDefaultAsync(
                      It.IsAny<Func<IQueryable<MyMapping>, IQueryable<MyMapping>>>(),
                      It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .Returns((Func<IQueryable<MyMapping>, IQueryable<MyMapping>> query, CancellationToken _, QueryOptions _) =>
                             new ValueTask<MyMapping?>(query(rows.AsQueryable()).FirstOrDefault()));
        var transaction = new Mock<IUnitOfWork>();
        transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mappings.Setup(r => r.Begin()).Returns(transaction.Object);
        var mutation = new Mock<IResourceMutation<MyMapping>>();
        mutation.Setup(m => m.CreateAsync(It.IsAny<MyMapping>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                .Callback((MyMapping row, IUnitOfWork _, CancellationToken _) => rows.Add(row))
                .ReturnsAsync(MutationResult.Applied);
        using var provider = new ServiceCollection().AddSingleton(mappings.Object).BuildServiceProvider();
        var subjects = new Mock<ISubjectIdentifierService>();
        subjects.Setup(s => s.Resolve("users/alice", application)).Returns("pairwise-1");
        var translator = new PairwiseSubjectTranslator<MyApplication, MyMapping>(
            new Mock<IApplicationManager<MyApplication>>().Object,
            subjects.Object,
            mappings.Object,
            mutation.Object,
            provider);

        var pairwise = await translator.EnsureMappingAsync(application, "users/alice");

        Assert.Equal("pairwise-1", pairwise);
        var row = Assert.Single(rows);
        Assert.IsType<MyMapping>(row);
        Assert.Equal("applications/client-1", row.Application);
        Assert.Equal("users/alice", row.Subject);
        Assert.Equal("pairwise-1", row.PairwiseSubject);
    }

    [Fact]
    public async Task Cascade_Application_Removal_Through_The_Host_Mapping_Entity() {
        var application = new MyApplication { ClientId = "client-1", CanonicalName = "applications/client-1" };
        var mapping     = new MyMapping { Application = "applications/client-1", Subject = "users/alice", PairwiseSubject = "pairwise-1" };
        var applications = Repository<MyApplication>();
        applications.Setup(r => r.RemoveAsync(application, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        var grants   = Repository<MyGrant>();
        var tokens   = Repository<MyToken>();
        var security = Repository<MySecurity>();
        var mappings = Repository<MyMapping>();
        grants.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<MyGrant>, IQueryable<MyGrant>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
              .Returns(Rows<MyGrant>());
        tokens.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<MyToken>, IQueryable<MyToken>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
              .Returns(Rows<MyToken>());
        security.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<MySecurity>, IQueryable<MySecurity>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .Returns(Rows<MySecurity>());
        mappings.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<MyMapping>, IQueryable<MyMapping>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .Returns(Rows(mapping));
        var mappingMutation = new Mock<IResourceMutation<MyMapping>>();
        mappingMutation.Setup(m => m.DeleteAsync(mapping, It.IsAny<IUnitOfWork>(), Operations.Delete, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(MutationResult.Applied);
        var services = new ServiceCollection();
        services.AddSingleton(applications.Object);
        services.AddSingleton(grants.Object);
        services.AddSingleton(tokens.Object);
        services.AddSingleton(security.Object);
        services.AddSingleton(mappings.Object);
        services.AddSingleton(mappingMutation.Object);
        services.AddSingleton(Mock.Of<IResourceMutation<MyGrant>>());
        services.AddSingleton(Mock.Of<IResourceMutation<MyToken>>());
        services.AddSingleton(Mock.Of<IResourceMutation<MySecurity>>());
        using var provider = services.BuildServiceProvider();
        var mutation = new ApplicationResourceMutation<MyApplication, MyGrant, MyMapping, MySecurity, MyToken>(provider);
        var transaction = new Mock<IUnitOfWork>();

        var result = await mutation.DeleteAsync(application, transaction.Object);

        Assert.Equal(MutationResult.Applied, result);
        mappingMutation.Verify(m => m.DeleteAsync(mapping, transaction.Object, Operations.Delete, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Mock<IRepository<T>> Repository<T>() where T : class {
        var repository = new Mock<IRepository<T>>();
        repository.SetupGet(r => r.AdviceContext)
                  .Returns(new AdviceContext(new ServiceCollection().BuildServiceProvider()));
        return repository;
    }

    private static async IAsyncEnumerable<T> Rows<T>(params T[] rows) {
        await Task.CompletedTask;
        foreach (var row in rows) {
            yield return row;
        }
    }

    [CanonicalName("applications/{application}")]
    public sealed class MyApplication : SchemataApplication;

    public sealed class MyGrant : SchemataAuthorization;

    [CanonicalName("scopes/{scope}")]
    public sealed class MyScope : SchemataScope;

    public sealed class MyMapping : SchemataSubjectMapping;

    public sealed class MySecurity : SchemataSecurity;

    [CanonicalName("tokens/{token}")]
    public sealed class MyToken : SchemataToken;
}
