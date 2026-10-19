using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class LinkHeaderShould : IClassFixture<WebAppFactory>
{
    private const string PayloadSecret   = "link-payload-secret";
    private const string ReferenceSecret = "link-reference-secret";

    private readonly WebAppFactory _factory;

    public LinkHeaderShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Application_Emits_Filtered_Token_List_And_Scope_List() {
        var scopeName = Unique("link-scope");
        var tokenName = Unique("link-token");
        var authorizationName = Unique("link-auth");
        var application = await SeedAsync(scopeName, tokenName, authorizationName);

        var client   = _factory.CreateClient();
        var response = await client.GetAsync("/v1/applications/test-client");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var links  = Links(response);
        var tokens = Assert.Single(links, link => link.Rel == "related" && link.Title == "tokens");
        Assert.Contains("/v1/tokens", tokens.Target);
        Assert.Equal($"application=\"{application}\"", Filter(tokens));

        var scopes = Assert.Single(links, link => link.Rel == "related" && link.Title == "scopes");
        Assert.EndsWith("/v1/scopes", Path(scopes.Target));

        var listed = await client.GetAsync(scopes.Target);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Contains(scopeName, await listed.Content.ReadAsStringAsync());

        var header = Header(response);
        Assert.DoesNotContain(PayloadSecret, header);
        Assert.DoesNotContain(ReferenceSecret, header);
    }

    [Fact]
    public async Task Token_Emits_Application_And_Persisted_Parent() {
        var scopeName = Unique("link-scope");
        var tokenName = Unique("link-token");
        var authorizationName = Unique("link-auth");
        var application = await SeedAsync(scopeName, tokenName, authorizationName);

        var client   = _factory.CreateClient();
        var response = await client.GetAsync($"/v1/tokens/{tokenName}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var links = Links(response);
        var app   = Assert.Single(links, link => link.Rel == "related" && link.Title == "application");
        Assert.EndsWith($"/v1/{application}", Path(app.Target));

        var parent = Assert.Single(links, link => link.Rel == "related" && link.Title == "parent");
        Assert.EndsWith($"/v1/authorizations/{authorizationName}", Path(parent.Target));

        var applicationResponse = await client.GetAsync(app.Target);
        Assert.Equal(HttpStatusCode.OK, applicationResponse.StatusCode);
        var parentResponse = await client.GetAsync(parent.Target);
        Assert.Equal(HttpStatusCode.OK, parentResponse.StatusCode);

        var header = Header(response);
        Assert.DoesNotContain(PayloadSecret, header);
        Assert.DoesNotContain(ReferenceSecret, header);
    }

    [Fact]
    public async Task Scope_Emits_Filtered_Application_List() {
        var scopeName = Unique("link-scope");
        var tokenName = Unique("link-token");
        await SeedAsync(scopeName, tokenName, Unique("link-auth"));

        var client   = _factory.CreateClient();
        var response = await client.GetAsync($"/v1/scopes/{scopeName}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var applications = Assert.Single(Links(response),
                                          link => link.Rel == "related" && link.Title == "applications");
        Assert.Contains("/v1/applications", applications.Target);
        Assert.Equal($"scope:\"{scopeName}\"", Filter(applications));
    }

    [Fact]
    public async Task Missing_Token_Has_No_Link_Header() {
        var response = await _factory.CreateClient().GetAsync("/v1/tokens/missing-link-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains(LinkHeader.Name));
    }

    private async Task<string> SeedAsync(string scopeName, string tokenName, string authorizationName) {
        using var scope = _factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var database = await factory.CreateDbContextAsync();

        var application = await database.Applications.SingleAsync(app => app.Name == "test-client");
        application.Scope = $"openid {scopeName}";
        database.Scopes.Add(new() {
            Uid           = Guid.NewGuid(),
            Name          = scopeName,
            CanonicalName = $"scopes/{scopeName}",
        });
        database.Authorizations.Add(new() {
            Uid           = Guid.NewGuid(),
            Name          = authorizationName,
            CanonicalName = $"authorizations/{authorizationName}",
            Application   = application.CanonicalName,
            Status        = "valid",
        });
        database.Tokens.Add(new() {
            Uid           = Guid.NewGuid(),
            Name          = tokenName,
            CanonicalName = $"tokens/{tokenName}",
            Type          = "access_token",
            Status        = "valid",
            Format        = "reference",
            ReferenceId   = $"{ReferenceSecret}-{tokenName}",
            Payload       = $$"""{"secret":"{{PayloadSecret}}"}""",
            Application   = application.CanonicalName,
            Parent        = $"authorizations/{authorizationName}",
        });
        await database.SaveChangesAsync();

        return application.CanonicalName!;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():n}";

    private static string Header(HttpResponseMessage response) {
        var values = response.Headers.GetValues(LinkHeader.Name).ToList();
        Assert.Single(values);
        return values[0];
    }

    private static IReadOnlyList<WebLink> Links(HttpResponseMessage response) {
        var links = LinkHeader.Parse(Header(response), response.RequestMessage!.RequestUri);
        Assert.Equal(links.Count, links.Select(Key).Distinct().Count());
        return links;
    }

    private static string Key(WebLink link) => string.Join("|",
        link.Target, link.Rel, link.Anchor, link.Hreflang, link.Media, link.Title, link.TitleStar, link.Type,
        link.Extensions is null
            ? string.Empty
            : string.Join(";", link.Extensions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                                              .Select(pair => pair.Key + "=" + pair.Value)));

    private static string Path(string target) => new Uri(target, UriKind.RelativeOrAbsolute).AbsolutePath;

    private static string? Filter(WebLink link) =>
        QueryHelpers.ParseQuery(new Uri(link.Target, UriKind.RelativeOrAbsolute).Query)["filter"].ToString();
}
