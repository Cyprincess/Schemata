using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Common;
using Schemata.Entity.Repository;

var options = new WebApplicationOptions { Args = args };

var builder = WebApplication.CreateBuilder(options);

var testApp = new SchemataApplication {
    Uid          = Identifiers.NewUid(),
    ClientId     = "test-client",
    ClientSecret = "test-secret",
    ClientType   = "confidential",
    Permissions  = new List<string> { "e:/Connect/Token", "g:client_credentials" },
};

var appMock = new Mock<IApplicationManager<SchemataApplication>>();
appMock.Setup(m => m.FindByClientIdAsync("test-client", It.IsAny<CancellationToken>())).ReturnsAsync(testApp);
appMock.Setup(m => m.ValidateClientSecretAsync(testApp, "test-secret", It.IsAny<CancellationToken>()))
       .ReturnsAsync(true);
appMock.Setup(m => m.ValidateClientSecretAsync(testApp, It.Is<string>(s => s != "test-secret"),
                                               It.IsAny<CancellationToken>()))
       .ReturnsAsync(false);
appMock.Setup(m => m.HasPermissionAsync(testApp, It.IsAny<string>(), It.IsAny<CancellationToken>()))
       .Returns((SchemataApplication app, string perm, CancellationToken _)
                    => Task.FromResult(app.Permissions!.Contains(perm)));

var scopeMock = new Mock<IScopeManager<SchemataScope>>();
scopeMock.Setup(m => m.ListAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
         .Returns(EmptyAsync<SchemataScope>());

var authzMock = new Mock<IAuthorizationManager<SchemataAuthorization>>();
authzMock.Setup(m => m.CreateAsync(It.IsAny<SchemataAuthorization>(), It.IsAny<CancellationToken>()))
         .Returns((SchemataAuthorization? a, CancellationToken _) => Task.FromResult(a));

var tokenMock = new Mock<ITokenManager<SchemataToken>>();
tokenMock.Setup(m => m.CreateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>()))
         .Returns((SchemataToken? t, CancellationToken _) => Task.FromResult(t));

// SchemataSubjectMapping repository is only exercised when an application uses
// SubjectTypes.Pairwise; the test client is confidential / public-sub so a no-op
// mock satisfies PairwiseSubjectTranslator's constructor without persisting.
var mappingMock = new Mock<IRepository<SchemataSubjectMapping>>();
mappingMock.Setup(m => m.AddAsync(It.IsAny<SchemataSubjectMapping>(), It.IsAny<CancellationToken>()))
           .Returns((SchemataSubjectMapping? m, CancellationToken _) => Task.FromResult(m));
mappingMock.Setup(m => m.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult(0));

builder.Services.AddSingleton(appMock.Object);
builder.Services.AddSingleton(scopeMock.Object);
builder.Services.AddSingleton(authzMock.Object);
builder.Services.AddSingleton(tokenMock.Object);
builder.Services.AddSingleton(mappingMock.Object);

builder.UseSchemata(schema => {
    schema.UseWellKnown();
    schema.UseAuthorization(o => {
               o.Issuer = "https://localhost";
               o.AddEphemeralSigningKey();
               o.AddEphemeralEncryptionKey();
               o.PermitResponseType("code");
           })
          .UseClientCredentialsFlow();
});

var app = builder.Build();

app.Run();

#pragma warning disable CS1998
static async IAsyncEnumerable<T> EmptyAsync<T>() { yield break; }
#pragma warning restore CS1998

namespace Schemata.Authorization.Integration.Tests
{
    public partial class Program;
}
