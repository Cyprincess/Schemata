using System;
using Schemata.Tenancy.Caching;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Tests;

[Trait("Layer", "Unit")]
public class TenantCacheKeyNormalizerShould
{
    [Fact]
    public void Normalize_Binds_The_Constructed_Uid_Regardless_Of_Ambient_Switches() {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var normalizer = new TenantCacheKeyNormalizer(a);

        using (TenantContext.Enter(new(b))) {
            Assert.Equal("11111111111111111111111111111111\x1ekey", normalizer.Normalize("key"));
        }

        Assert.Equal("11111111111111111111111111111111\x1ekey", normalizer.Normalize("key"));
    }

    [Fact]
    public void Different_Uids_Separate_The_Same_Key_Into_Different_Namespaces() {
        var a = new TenantCacheKeyNormalizer(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var b = new TenantCacheKeyNormalizer(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.NotEqual(a.Normalize("key"), b.Normalize("key"));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("")]
    [InlineData("nested\x1ekey")]
    public void Normalize_Preserves_The_Key_Body_After_The_Namespace_Prefix(string key) {
        var normalizer = new TenantCacheKeyNormalizer(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        Assert.Equal("33333333333333333333333333333333\x1e" + key, normalizer.Normalize(key));
    }
}
