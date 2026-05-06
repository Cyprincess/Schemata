using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceSanitizeShould
{
    [Fact]
    public async Task Create_Sanitize_ClearsSystemManagedFields() {
        var request = new ManagedRequest {
            Name        = "managed/forged",
            Owner       = "hacker",
            State       = "Active",
            Uid         = "uid-forged",
            CreateTime  = DateTimeOffset.UtcNow,
            UpdateTime  = DateTimeOffset.UtcNow,
            DeleteTime  = DateTimeOffset.UtcNow,
            PurgeTime   = DateTimeOffset.UtcNow,
            Reconciling = true,
            Parent      = "parents/1",
            DisplayName = "keep-me",
        };

        var advisor   = new AdviceCreateRequestSanitize<ManagedEntity, ManagedRequest>();
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<ManagedEntity>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(request.Name);
        Assert.Null(request.Uid);
        Assert.Null(request.Owner);
        Assert.Null(request.State);
        Assert.Equal(default, request.CreateTime);
        Assert.Equal(default, request.UpdateTime);
        Assert.Equal(default, request.DeleteTime);
        Assert.Equal(default, request.PurgeTime);
        // Create-time sanitize leaves "parent" alone (Create legally accepts a parent).
        Assert.Equal("parents/1", request.Parent);
        // Non-system fields are untouched.
        Assert.Equal("keep-me", request.DisplayName);
    }

    [Fact]
    public async Task Update_Sanitize_ClearsParentAndStripsMaskEntries() {
        var request = new ManagedRequest {
            Name        = "managed/target",
            Owner       = "hacker",
            Parent      = "parents/new",
            DisplayName = "keep-me",
            UpdateMask  = "display_name,owner,parent,name",
        };

        var advisor   = new AdviceUpdateRequestSanitize<ManagedEntity, ManagedRequest>();
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<ManagedEntity>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(request.Name);
        Assert.Null(request.Owner);
        Assert.Equal("parents/new", request.Parent);
        Assert.Equal("keep-me", request.DisplayName);
        Assert.Equal("display_name,parent", request.UpdateMask);
    }

    [Fact]
    public async Task Update_Sanitize_EmptyMask_StaysEmpty() {
        var request = new ManagedRequest { Name = "managed/target", UpdateMask = "" };

        var advisor = new AdviceUpdateRequestSanitize<ManagedEntity, ManagedRequest>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());

        await advisor.AdviseAsync(ctx, request, new(), null);

        Assert.Equal("", request.UpdateMask);
    }

    [Fact]
    public async Task Create_Sanitize_RequestWithoutSystemProperties_DoesNotThrow() {
        var request = new MinimalRequest { Name = "minimal/1", Label = "preserve" };

        var advisor = new AdviceCreateRequestSanitize<MinimalEntity, MinimalRequest>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());

        var result = await advisor.AdviseAsync(ctx, request, new(), null);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(request.Name);
        Assert.Equal("preserve", request.Label);
    }

    #region Fixtures

    public sealed class ManagedEntity : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    public sealed class ManagedRequest : ICanonicalName, IUpdateMask
    {
        public string?        Owner       { get; set; }
        public string?        State       { get; set; }
        public string?        Uid         { get; set; }
        public DateTimeOffset CreateTime  { get; set; }
        public DateTimeOffset UpdateTime  { get; set; }
        public DateTimeOffset DeleteTime  { get; set; }
        public DateTimeOffset PurgeTime   { get; set; }
        public bool           Reconciling { get; set; }
        public string?        Parent      { get; set; }
        public string?        DisplayName { get; set; }

        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion

        #region IUpdateMask Members

        public string? UpdateMask { get; set; }

        #endregion
    }

    public sealed class MinimalEntity : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    public sealed class MinimalRequest : ICanonicalName
    {
        public string? Label { get; set; }

        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion
}
