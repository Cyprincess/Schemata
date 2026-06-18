using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceReadMaskShould
{
    [Fact]
    public async Task Response_WithMask_TrimsUnlistedFields() {
        var advisor = new AdviceResponseReadMask<Student, Student>();
        var ctx     = Ctx(new("full_name"));
        var detail  = new Student { FullName = "Alice", Age = 18, Name = "alice-1" };

        await advisor.AdviseAsync(ctx, new(), detail, null);

        Assert.Equal("Alice", detail.FullName);
        Assert.Equal(0, detail.Age);
        Assert.Null(detail.Name);
    }

    [Fact]
    public async Task Response_WithNameAndEtagMask_KeepsCanonicalNameAndEntityTag() {
        var advisor = new AdviceResponseReadMask<Student, Student>();
        var ctx     = Ctx(new("name,etag"));
        var detail = new Student {
            FullName      = "Alice",
            Age           = 18,
            Name          = "alice-1",
            CanonicalName = "students/alice-1",
            EntityTag     = "v1",
        };

        await advisor.AdviseAsync(ctx, new(), detail, null);

        // Wire "name" resolves to CanonicalName and "etag" to EntityTag; the internal Name and every
        // unlisted field are trimmed.
        Assert.Equal("students/alice-1", detail.CanonicalName);
        Assert.Equal("v1", detail.EntityTag);
        Assert.Null(detail.Name);
        Assert.Null(detail.FullName);
        Assert.Equal(0, detail.Age);
    }

    [Fact]
    public async Task Response_WithoutMarker_LeavesDetailIntact() {
        var advisor = new AdviceResponseReadMask<Student, Student>();
        var ctx     = Ctx(null);
        var detail  = new Student { FullName = "Alice", Age = 18 };

        await advisor.AdviseAsync(ctx, new(), detail, null);

        Assert.Equal("Alice", detail.FullName);
        Assert.Equal(18, detail.Age);
    }

    [Fact]
    public async Task Response_UnknownPath_ThrowsValidation() {
        var advisor = new AdviceResponseReadMask<Student, Student>();
        var ctx     = Ctx(new("no_such_field"));

        await Assert.ThrowsAsync<ValidationException>(
            () => advisor.AdviseAsync(ctx, new(), new(), null));
    }

    [Fact]
    public async Task Response_NestedPath_TrimsNestedObject() {
        var advisor = new AdviceResponseReadMask<Student, Student>();
        var ctx     = Ctx(new("profile.display_name"));
        var detail = new Student {
            FullName = "Alice",
            Profile  = new() { DisplayName = "Visible", Bio = "Hidden", Locale = "en" },
        };

        await advisor.AdviseAsync(ctx, new(), detail, null);

        Assert.Null(detail.FullName);
        Assert.NotNull(detail.Profile);
        Assert.Equal("Visible", detail.Profile.DisplayName);
        Assert.Null(detail.Profile.Bio);
        Assert.Null(detail.Profile.Locale);
    }

    [Fact]
    public async Task Response_NestedStringTraversal_ThrowsValidation() {
        var advisor = new AdviceResponseReadMask<Student, Student>();
        var ctx     = Ctx(new("full_name.value"));

        await Assert.ThrowsAsync<ValidationException>(
            () => advisor.AdviseAsync(ctx, new(), new(), null));
    }

    [Fact]
    public async Task Response_InvalidNestedPath_ThrowsValidation() {
        var advisor = new AdviceResponseReadMask<Student, Student>();
        var ctx     = Ctx(new("profile.no_such_field"));

        await Assert.ThrowsAsync<ValidationException>(
            () => advisor.AdviseAsync(ctx, new(), new(), null));
    }

    [Fact]
    public async Task ListResponse_WithMask_TrimsEverySummary() {
        var advisor = new AdviceListResponseReadMask<Student>();
        var ctx     = Ctx(new("age"));
        var summaries = ImmutableArray.Create(
            new() { FullName = "Alice", Age = 18 },
            new Student { FullName = "Bob", Age   = 19 });

        await advisor.AdviseAsync(ctx, summaries, null);

        Assert.All(summaries, s => Assert.Null(s.FullName));
        Assert.Equal(18, summaries[0].Age);
        Assert.Equal(19, summaries[1].Age);
    }

    [Fact]
    public async Task ListResponse_NestedCollectionPath_TrimsEveryElement() {
        var advisor = new AdviceListResponseReadMask<Student>();
        var ctx     = Ctx(new("courses.title"));
        var summaries = ImmutableArray.Create(new Student {
            FullName = "Alice",
            Courses = [
                new() { Title = "Math", Code = "M101" },
                new() { Title = "History", Code = "H202" },
            ],
        });

        await advisor.AdviseAsync(ctx, summaries, null);

        Assert.Null(summaries[0].FullName);
        Assert.Equal("Math", summaries[0].Courses[0].Title);
        Assert.Null(summaries[0].Courses[0].Code);
        Assert.Equal("History", summaries[0].Courses[1].Title);
        Assert.Null(summaries[0].Courses[1].Code);
    }

    private static AdviceContext Ctx(ReadMaskRequested? mask) {
        var ctx = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        if (mask is not null) {
            ctx.Set(mask);
        }

        return ctx;
    }
}
