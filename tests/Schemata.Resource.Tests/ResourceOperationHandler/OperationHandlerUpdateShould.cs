using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceOperationHandler;

public class OperationHandlerUpdateShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Update_ValidRequest_UpdatesEntityAndCommits() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        var request = new Student { FullName = "Alice Updated", Age = entity.Age, Grade = entity.Grade };

        var result = await handler.UpdateAsync(entity.CanonicalName!, request, null, null);

        Assert.NotNull(result.Detail);
        _fixture.Repository.Verify(r => r.UpdateAsync(It.IsAny<Student>(), CancellationToken.None), Times.Once);
        _fixture.Repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Update_WithUpdateMask_OnlyAppliesMaskedFields() {
        var handler  = _fixture.CreateHandler();
        var entity   = _fixture.Students[0];
        var original = entity.Age;
        var request = new Student {
            FullName = "Alice Renamed", Age = 999, UpdateMask = "FullName",
        };

        await handler.UpdateAsync(entity.CanonicalName!, request, null, null);

        Assert.Equal("Alice Renamed", entity.FullName);
        Assert.Equal(original, entity.Age);
    }

    [Fact]
    public async Task Update_WithUnknownMaskPath_ThrowsValidationException() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        var request = new Student { FullName = "Renamed", UpdateMask = "no_such_field" };

        await Assert.ThrowsAsync<ValidationException>(() => handler.UpdateAsync(
                                                          entity.CanonicalName!, request, null, null));
    }

    [Fact]
    public async Task Update_WithNestedMaskPath_OnlyAppliesNestedLeaf() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        entity.Profile = new() { DisplayName = "Old Name", Bio = "Old Bio", Locale = "en" };
        var request = new Student {
            FullName   = "Renamed",
            Age        = 999,
            Profile    = new() { DisplayName = "New Name", Bio = "New Bio", Locale = "fr" },
            UpdateMask = "profile.display_name",
        };

        await handler.UpdateAsync(entity.CanonicalName!, request, null, null);

        Assert.Equal("Alice", entity.FullName);
        Assert.Equal(18, entity.Age);
        Assert.NotNull(entity.Profile);
        Assert.Equal("New Name", entity.Profile.DisplayName);
        Assert.Equal("Old Bio", entity.Profile.Bio);
        Assert.Equal("en", entity.Profile.Locale);
    }

    [Fact]
    public async Task Update_WithNestedNullMaskedLeaf_ClearsLeaf() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        entity.Profile = new() { DisplayName = "Old Name", Bio = "Old Bio", Locale = "en" };
        var request = new Student {
            Profile    = new() { DisplayName = null, Bio = "New Bio", Locale = "fr" },
            UpdateMask = "profile.display_name",
        };

        await handler.UpdateAsync(entity.CanonicalName!, request, null, null);

        Assert.NotNull(entity.Profile);
        Assert.Null(entity.Profile.DisplayName);
        Assert.Equal("Old Bio", entity.Profile.Bio);
        Assert.Equal("en", entity.Profile.Locale);
    }

    [Fact]
    public async Task Update_WithNestedCollectionMaskPath_ThrowsValidationException() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        var request = new Student { UpdateMask = "courses.title" };

        await Assert.ThrowsAsync<ValidationException>(() => handler.UpdateAsync(
                                                          entity.CanonicalName!, request, null, null));
    }

    [Fact]
    public async Task Update_WithWildcardMask_AppliesFullReplacement() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        var request = new Student {
            CanonicalName = entity.CanonicalName,
            FullName      = "Alice Replaced",
            Age           = 42,
            UpdateMask    = "*",
        };

        var result = await handler.UpdateAsync(entity.CanonicalName!, request, null, null);

        Assert.NotNull(result.Detail);
        Assert.Equal("Alice Replaced", entity.FullName);
        Assert.Equal(42, entity.Age);
    }

    [Fact]
    public async Task Update_Missing_ThrowsNotFound() {
        var handler = _fixture.CreateHandler();
        var request = new Student { FullName = "Ghost" };

        await Assert.ThrowsAsync<NotFoundException>(() => handler.UpdateAsync(
                                                        "students/zoe-9", request, null, null));
    }

    [Fact]
    public async Task Update_MissingWithAllowMissing_CreatesResource() {
        var handler = _fixture.CreateHandler();
        var request = new Student {
            FullName     = "Zoe",
            Age          = 23,
            UpdateMask   = "full_name",
            AllowMissing = true,
        };

        var result = await handler.UpdateAsync("students/zoe-9", request, null, null);

        Assert.NotNull(result.Detail);
        var created = Assert.Single(_fixture.Students, s => s.Name == "zoe-9");
        Assert.Equal("students/zoe-9", created.CanonicalName);
        Assert.Equal("Zoe", created.FullName);
        // AIP-134: creation via allow_missing applies every field, ignoring the mask.
        Assert.Equal(23, created.Age);
    }

    [Fact]
    public async Task Update_ETagMismatch_ThrowsConcurrencyException() {
        var handler = _fixture.CreateHandler(services => {
            services.TryAddScoped<IResourceUpdateAdvisor<Student, Student>, AdviceUpdateFreshness<Student, Student>>();
        });
        var entity  = _fixture.Students[0];
        var request = new Student { EntityTag = "W/\"wrongtag\"" };

        await Assert.ThrowsAsync<ConcurrencyException>(() => handler.UpdateAsync(
                                                           entity.CanonicalName!, request, null, null));
    }
}
