using System.Linq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Common.Tests.Errors;

public class SchemataResourceErrorsShould
{
    [Fact]
    public void NotFound_Sets_ResourceType_To_Singular_And_Omits_Owner() {
        var exception = SchemataResourceErrors.NotFound<Book>("books/les-miserables");

        Assert.IsType<NotFoundException>(exception);
        Assert.Equal(ErrorCodes.NotFound, exception.Status);

        var resource = AssertSingle<ResourceInfoDetail>(exception);
        Assert.Equal("Book", resource.ResourceType);
        Assert.Equal("books/les-miserables", resource.ResourceName);
        Assert.Null(resource.Owner);

        var info = AssertSingle<ErrorInfoDetail>(exception);
        Assert.Equal(ErrorReasons.ResourceNotFound, info.Reason);
    }

    [Fact]
    public void AlreadyExists_Sets_ResourceInfo_And_Reason() {
        var exception = SchemataResourceErrors.AlreadyExists<Book>("books/duplicate");

        Assert.IsType<AlreadyExistsException>(exception);
        Assert.Equal(ErrorCodes.AlreadyExists, exception.Status);

        var resource = AssertSingle<ResourceInfoDetail>(exception);
        Assert.Equal("Book", resource.ResourceType);
        Assert.Equal("books/duplicate", resource.ResourceName);
        Assert.Null(resource.Owner);

        var info = AssertSingle<ErrorInfoDetail>(exception);
        Assert.Equal(ErrorReasons.ResourceAlreadyExists, info.Reason);
    }

    [Fact]
    public void PreconditionFailed_Attaches_PreconditionFailure_With_Single_Violation() {
        var exception = SchemataResourceErrors.PreconditionFailed<Book>(
            "books/x",
            "ETAG_MISMATCH",
            "expected etag did not match");

        Assert.IsType<FailedPreconditionException>(exception);
        Assert.Equal(ErrorCodes.FailedPrecondition, exception.Status);

        var resource = AssertSingle<ResourceInfoDetail>(exception);
        Assert.Equal("Book", resource.ResourceType);
        Assert.Equal("books/x", resource.ResourceName);

        var precondition = AssertSingle<PreconditionFailureDetail>(exception);
        var violation    = Assert.Single(precondition.Violations!);
        Assert.Equal("Book", violation.Type);
        Assert.Equal("ETAG_MISMATCH", violation.Subject);
        Assert.Equal("expected etag did not match", violation.Description);

        var info = AssertSingle<ErrorInfoDetail>(exception);
        Assert.Equal(ErrorReasons.PreconditionNotSatisfied, info.Reason);
    }

    [Fact]
    public void PermissionDenied_Sets_Owner_When_Provided() {
        var exception = SchemataResourceErrors.PermissionDenied<Book>(
            "books/x",
            "users/alice");

        Assert.IsType<PermissionDeniedException>(exception);
        Assert.Equal(ErrorCodes.PermissionDenied, exception.Status);

        var resource = AssertSingle<ResourceInfoDetail>(exception);
        Assert.Equal("Book", resource.ResourceType);
        Assert.Equal("books/x", resource.ResourceName);
        Assert.Equal("users/alice", resource.Owner);

        var info = AssertSingle<ErrorInfoDetail>(exception);
        Assert.Equal(ErrorReasons.InsufficientPermission, info.Reason);
    }

    [Fact]
    public void PermissionDenied_Without_Owner_Leaves_Owner_Null() {
        var exception = SchemataResourceErrors.PermissionDenied<Book>("books/x");

        var resource = AssertSingle<ResourceInfoDetail>(exception);
        Assert.Null(resource.Owner);
    }

    [Fact]
    public void Aborted_Sets_ResourceInfo_And_ConcurrencyMismatch_Reason() {
        var exception = SchemataResourceErrors.Aborted<Book>("books/x");

        Assert.IsType<AbortedException>(exception);
        Assert.Equal(ErrorCodes.Aborted, exception.Status);

        var resource = AssertSingle<ResourceInfoDetail>(exception);
        Assert.Equal("Book", resource.ResourceType);
        Assert.Equal("books/x", resource.ResourceName);

        var info = AssertSingle<ErrorInfoDetail>(exception);
        Assert.Equal(ErrorReasons.ConcurrencyMismatch, info.Reason);
    }

    [Fact]
    public void All_Factories_Produce_English_Invariant_Message() {
        var notFound   = SchemataResourceErrors.NotFound<Book>("books/x");
        var conflict   = SchemataResourceErrors.AlreadyExists<Book>("books/x");
        var denied     = SchemataResourceErrors.PermissionDenied<Book>("books/x");
        var aborted    = SchemataResourceErrors.Aborted<Book>("books/x");
        var blocked    = SchemataResourceErrors.PreconditionFailed<Book>("books/x", "SOFT_DELETED");

        Assert.False(string.IsNullOrWhiteSpace(notFound.Message));
        Assert.False(string.IsNullOrWhiteSpace(conflict.Message));
        Assert.False(string.IsNullOrWhiteSpace(denied.Message));
        Assert.False(string.IsNullOrWhiteSpace(aborted.Message));
        Assert.False(string.IsNullOrWhiteSpace(blocked.Message));
    }

    private static TDetail AssertSingle<TDetail>(SchemataException exception)
        where TDetail : class, IErrorDetail {
        Assert.NotNull(exception.Details);
        var match = exception.Details!.OfType<TDetail>().ToList();
        return Assert.Single(match);
    }

    #region Nested type: Book

    private sealed class Book
    {
        public string? Name { get; set; }
    }

    #endregion
}
