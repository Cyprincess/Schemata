using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Binding;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class DuplicateParameterShould
{
    private static readonly (PropertyInfo Prop, string Param)[] Map =
        OAuthBinderHelpers.BuildMap(typeof(AuthorizeRequest));

    [Fact]
    public void Reject_A_Parameter_Provided_Twice() {
        var form = new Dictionary<string, StringValues> {
            [Parameters.ClientId] = new(["client-1", "client-2"]),
        };

        var ex = Assert.Throws<OAuthException>(() => OAuthBinderHelpers.ThrowIfDuplicateParameters(form, Map));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    [Fact]
    public void Ignore_Unmapped_And_Single_Valued_Parameters() {
        var form = new Dictionary<string, StringValues> {
            [Parameters.ClientId] = "client-1",
            ["custom"]            = new(["a", "b"]),
        };

        OAuthBinderHelpers.ThrowIfDuplicateParameters(form, Map);
    }
}