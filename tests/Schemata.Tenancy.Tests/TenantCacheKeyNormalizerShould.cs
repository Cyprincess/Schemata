using System;
using Schemata.Tenancy.Caching;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Tests;

[Trait("Layer", "Unit")]
public class TenantCacheKeyNormalizerShould
{
    [Fact]
    public void Tenant_Keys_Use_The_Versioned_Tenant_Partition() {
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var normalizer = new TenantCacheKeyNormalizer(new TenantIdentity(a));

        Assert.Equal("v1\u001etenant\u001e11111111111111111111111111111111\u001ekey", normalizer.Normalize("key"));
    }

    [Fact]
    public void Host_Keys_Use_The_Reserved_Host_Partition() {
        var normalizer = new TenantCacheKeyNormalizer(TenantIdentity.Host);

        Assert.Equal("v1\u001ehost\u001ekey", normalizer.Normalize("key"));
    }

    [Fact]
    public void Different_Uids_Separate_The_Same_Key_Into_Different_Namespaces() {
        var a = new TenantCacheKeyNormalizer(new TenantIdentity(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        var b = new TenantCacheKeyNormalizer(new TenantIdentity(Guid.Parse("22222222-2222-2222-2222-222222222222")));

        Assert.NotEqual(a.Normalize("key"), b.Normalize("key"));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("")]
    [InlineData("nested\u001ekey")]
    public void Normalize_Preserves_The_Key_Body_After_The_Namespace_Prefix(string key) {
        var normalizer = new TenantCacheKeyNormalizer(new TenantIdentity(Guid.Parse("33333333-3333-3333-3333-333333333333")));

        Assert.Equal("v1\u001etenant\u001e33333333333333333333333333333333\u001e" + key, normalizer.Normalize(key));
    }
}
