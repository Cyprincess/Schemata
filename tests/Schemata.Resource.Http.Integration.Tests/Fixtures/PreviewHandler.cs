using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

/// <summary>
///     Read-only <c>:preview</c> custom method used to exercise GET routing.
/// </summary>
public sealed class PreviewHandler : IResourceMethodHandler<Student, EmptyResourceRequest, Student>
{
    #region IResourceMethodHandler<Student, EmptyResourceRequest, Student> Members

    public ValueTask<Student> InvokeAsync(
        string?              name,
        EmptyResourceRequest request,
        Student?             entity,
        ClaimsPrincipal?     principal,
        CancellationToken    ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        return ValueTask.FromResult(entity);
    }

    #endregion
}
