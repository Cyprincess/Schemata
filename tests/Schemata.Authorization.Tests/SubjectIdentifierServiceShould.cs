using System;
using System.Collections.Generic;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using Options = Microsoft.Extensions.Options.Options;

namespace Schemata.Authorization.Tests;

public class SubjectIdentifierServiceShould
{
    private const string Salt = "test-salt-value";

    private static SchemataApplication CreateApplication(
        string? subjectType         = null,
        string? sectorIdentifierUri = null,
        string  redirectUri         = "https://app.example.com/callback"
    ) {
        var application = new SchemataApplication {
            SubjectType         = subjectType,
            SectorIdentifierUri = sectorIdentifierUri,
            RedirectUris        = new List<string> { redirectUri },
        };
        return application;
    }

    private static SubjectIdentifierService CreateService(
        string  subjectType  = SubjectTypes.Public,
        string? pairwiseSalt = null
    ) {
        if (subjectType == null) throw new ArgumentNullException(nameof(subjectType));
        var options = Options.Create(new SchemataAuthorizationOptions {
            SubjectType = subjectType, PairwiseSalt = pairwiseSalt,
        });
        return new(options);
    }

    [Fact]
    public void Resolve_PublicType_ReturnsUserId() {
        var service     = CreateService();
        var application = CreateApplication();

        var result = service.Resolve("user-42", application);

        Assert.Equal("user-42", result);
    }

    [Fact]
    public void Resolve_PairwiseType_ReturnsDeterministicHash() {
        var service     = CreateService(SubjectTypes.Pairwise, Salt);
        var application = CreateApplication();

        var first  = service.Resolve("user-42", application);
        var second = service.Resolve("user-42", application);

        Assert.Equal(first, second);
        Assert.NotEqual("user-42", first);
    }

    [Fact]
    public void Resolve_PairwiseType_DifferentSectors_ProduceDifferentSubs() {
        var service = CreateService(SubjectTypes.Pairwise, Salt);
        var alpha   = CreateApplication(redirectUri: "https://alpha.example.com/callback");
        var beta    = CreateApplication(redirectUri: "https://beta.example.com/callback");

        var first  = service.Resolve("user-42", alpha);
        var second = service.Resolve("user-42", beta);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Resolve_PairwiseType_DifferentUsers_ProduceDifferentSubs() {
        var service     = CreateService(SubjectTypes.Pairwise, Salt);
        var application = CreateApplication();

        var first  = service.Resolve("user-1", application);
        var second = service.Resolve("user-2", application);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Resolve_AppOverride_UsesAppSubjectType() {
        var service     = CreateService(SubjectTypes.Public, Salt);
        var application = CreateApplication(SubjectTypes.Pairwise);

        var result = service.Resolve("user-42", application);

        Assert.NotEqual("user-42", result);
    }

    [Fact]
    public void Resolve_AppNoOverride_UsesGlobalDefault() {
        var service     = CreateService(SubjectTypes.Pairwise, Salt);
        var application = CreateApplication();

        var result = service.Resolve("user-42", application);

        Assert.NotEqual("user-42", result);
    }
}
