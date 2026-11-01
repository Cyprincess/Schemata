using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Schemata.Common.Tests;

public class AppDomainTypeCacheShould
{
    [Fact]
    public void GetFields_Returns_Public_Instance_Fields_And_Reuses_The_Cached_Dictionary() {
        var first  = AppDomainTypeCache.GetFields(typeof(FieldSample));
        var second = AppDomainTypeCache.GetFields(typeof(FieldSample));

        Assert.Same(first, second);
        Assert.True(first.ContainsKey(nameof(FieldSample.Instance)));
        Assert.False(first.ContainsKey(nameof(FieldSample.Static)));
    }

    [Fact]
    public void GetProperty_Returns_Null_When_The_Property_Does_Not_Exist() {
        Assert.Null(AppDomainTypeCache.GetProperty(typeof(string), "NoSuchPropertyOnString"));
    }

    [Fact]
    public void GetAllTypes_Refreshes_Assemblies_Loaded_After_The_First_Touch() {
        // Force the cache to initialize and index the assemblies loaded so far.
        AppDomainTypeCache.GetProperties(typeof(string));

        // Moq is referenced by this test project but not used by other tests in it; load it
        // explicitly so its types only become discoverable through the incremental refresh.
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                 .FirstOrDefault(candidate => candidate.GetName().Name == "Moq")
                    ?? Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Moq.dll"));

        var types = AppDomainTypeCache.GetAllTypes().Select(type => type.FullName).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Moq.Mock", types);
        Assert.All(
            assembly.GetExportedTypes(),
            type => Assert.Contains(type.FullName, types));
    }

    private sealed class FieldSample
    {
        public static int Static = 0;

        public int Instance = 0;
    }
}
