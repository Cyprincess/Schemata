using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Xunit;

namespace Schemata.Authorization.Tests;

public class ApplicationClientNameShould
{
    [Fact]
    public void Store_The_Standard_Client_Name_While_Satisfying_IDescriptive() {
        var app = new SchemataApplication { ClientName = "My SPA" };

        Assert.Equal("My SPA", app.ClientName);
        Assert.Equal("My SPA", ((IDescriptive)app).DisplayName);
    }
}