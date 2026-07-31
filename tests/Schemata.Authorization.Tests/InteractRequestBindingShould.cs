using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Moq;
using Schemata.Authorization.Foundation.Binding;
using Schemata.Authorization.Skeleton.Models;
using Xunit;

namespace Schemata.Authorization.Tests;

public class InteractRequestBindingShould
{
    [Fact]
    public async Task BindTheDeviceUserCodeFromItsRfc8628ParameterName() {
        var request = await BindAsync(new() { ["user_code"] = "WDJB-MJHT", ["code_type"] = "urn:user_code" });

        Assert.Equal("WDJB-MJHT", request.UserCode);
        Assert.Equal("urn:user_code", request.CodeType);
        Assert.Null(request.Code);
    }

    [Fact]
    public async Task BindTheInteractionCodeFromItsOwnParameterName() {
        var request = await BindAsync(new() { ["code"] = "abc123", ["code_type"] = "urn:interaction" });

        Assert.Equal("abc123", request.Code);
        Assert.Null(request.UserCode);
    }

    private static async Task<InteractRequest> BindAsync(Dictionary<string, StringValues> query) {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(query);

        var binding = new Mock<ModelBindingContext>();
        binding.SetupGet(b => b.HttpContext).Returns(context);
        binding.SetupProperty(b => b.Result);

        await new OAuthQueryBinder<InteractRequest>().BindModelAsync(binding.Object);

        return Assert.IsType<InteractRequest>(binding.Object.Result.Model);
    }
}
