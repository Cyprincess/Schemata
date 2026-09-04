using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Binding;
using Schemata.Authorization.Skeleton.Models;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class ResourceBindingShould
{
    private static readonly (PropertyInfo Prop, string Param)[] AuthorizeMap =
        OAuthBinderHelpers.BuildMap(typeof(AuthorizeRequest));

    private static readonly (PropertyInfo Prop, string Param)[] TokenMap =
        OAuthBinderHelpers.BuildMap(typeof(TokenRequest));

    [Fact]
    public async Task Bind_Repeated_Resource_Parameters_From_The_Query_As_A_Collection() {
        var request = await BindQueryAsync(new(new Dictionary<string, StringValues> {
            [Parameters.Resource] = new(["https://cal.example.com/", "https://contacts.example.com/"]),
        }));

        Assert.Equal(
            new[] { "https://cal.example.com/", "https://contacts.example.com/" },
            request.Resource);
    }

    [Fact]
    public async Task Bind_Repeated_Resource_Parameters_From_The_Form_As_A_Collection() {
        var request = await BindFormAsync(new(new() {
            [Parameters.Resource] = new(["https://cal.example.com/", "https://contacts.example.com/"]),
        }));

        Assert.Equal(
            new[] { "https://cal.example.com/", "https://contacts.example.com/" },
            request.Resource);
    }

    [Fact]
    public async Task Bind_A_Single_Resource_Parameter_As_A_One_Element_Collection() {
        var request = await BindQueryAsync(new(new Dictionary<string, StringValues> {
            [Parameters.Resource] = "https://api.example.com/",
        }));

        Assert.Equal(new[] { "https://api.example.com/" }, request.Resource);
    }

    [Fact]
    public async Task Leave_Resource_Unset_When_The_Parameter_Is_Absent() {
        var request = await BindQueryAsync(new(new Dictionary<string, StringValues> {
            [Parameters.ResponseType] = "code",
        }));

        Assert.Null(request.Resource);
    }

    [Fact]
    public async Task Skip_Empty_Entries_When_Collecting_Repeated_Resource_Parameters() {
        var request = await BindQueryAsync(new(new Dictionary<string, StringValues> {
            [Parameters.Resource] = new(["", "https://cal.example.com/"]),
        }));

        Assert.Equal(new[] { "https://cal.example.com/" }, request.Resource);
    }

    [Fact]
    public void Accept_Repeated_Resource_Parameters_In_Duplicate_Detection() {
        var query = new Dictionary<string, StringValues> {
            [Parameters.Resource] = new(["https://cal.example.com/", "https://contacts.example.com/"]),
        };

        OAuthBinderHelpers.ThrowIfDuplicateParameters(query, AuthorizeMap);
        OAuthBinderHelpers.ThrowIfDuplicateParameters(query, TokenMap);
    }

    [Fact]
    public void Still_Reject_Duplicated_Single_Value_Parameters() {
        var query = new Dictionary<string, StringValues> {
            ["scope"] = new(["calendar", "contacts"]),
        };

        var ex = Assert.Throws<OAuthException>(
            () => OAuthBinderHelpers.ThrowIfDuplicateParameters(query, AuthorizeMap));

        Assert.Equal(OAuthErrors.InvalidRequest, ex.Status);
    }

    private static async Task<AuthorizeRequest> BindQueryAsync(QueryCollection query) {
        var context = new DefaultHttpContext();
        context.Request.Query = query;

        var binding = new Mock<ModelBindingContext>();
        binding.SetupGet(b => b.HttpContext).Returns(context);
        binding.SetupProperty(b => b.Result);

        await new OAuthQueryBinder<AuthorizeRequest>().BindModelAsync(binding.Object);

        return Assert.IsType<AuthorizeRequest>(binding.Object.Result.Model);
    }

    private static async Task<TokenRequest> BindFormAsync(FormCollection form) {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = form;

        var binding = new Mock<ModelBindingContext>();
        binding.SetupGet(b => b.HttpContext).Returns(context);
        binding.SetupProperty(b => b.Result);

        await new OAuthFormBinder<TokenRequest>().BindModelAsync(binding.Object);

        return Assert.IsType<TokenRequest>(binding.Object.Result.Model);
    }
}
